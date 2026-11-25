using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CrowCast.Backends;
using CrowCast.Configuration;
using CrowCast.Diagnostics;
using CrowCast.Events;
using CrowCast.Membership;
using CrowCast.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrowCast.Internal;

internal sealed class CrowCastService : ICrowCast, ICrowCastDiagnostics, IHostedService, IAsyncDisposable
{
    private readonly ICrowCastBackend _backend;
    private readonly CrowCastOptions _options;
    private readonly IReadOnlyCollection<ICrowCastEventHandler> _eventHandlers;
    private readonly ILogger<CrowCastService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<CrowCastNodeIdentity, CrowCastMember> _members = [];
    private readonly object _membershipGate = new();
    private readonly ConcurrentDictionary<CrowCastMessageId, DateTimeOffset> _deduplication = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly CrowCastTelemetry _telemetry;
    private readonly CrowCastFixedWindowRateLimiter _receiveRateLimiter;
    private CrowCastNodeIdentity? _coordinator;
    private int _started;
    private int _disposed;
    private int _messagesSinceCleanup;
    private long _rejectionLogCount;

    public CrowCastService(
        ICrowCastBackend backend,
        IOptions<CrowCastOptions> options,
        IEnumerable<ICrowCastEventHandler> eventHandlers,
        ILogger<CrowCastService> logger,
        TimeProvider timeProvider)
    {
        _backend = backend;
        _options = options.Value;
        _eventHandlers = eventHandlers.ToArray();
        _logger = logger;
        _timeProvider = timeProvider;
        LocalNode = CrowCastNodeIdentity.New();
        _telemetry = new CrowCastTelemetry(() => Members);
        _receiveRateLimiter = new CrowCastFixedWindowRateLimiter(
            _options.MaximumReceivedMessagesPerInterval,
            _options.ReceivedMessageRateLimitInterval,
            _timeProvider.GetUtcNow());
    }

    public CrowCastNodeIdentity LocalNode { get; }

    public IReadOnlyCollection<CrowCastMember> Members
    {
        get
        {
            lock (_membershipGate)
            {
                return _members.Values.OrderBy(member => member.Identity).ToArray();
            }
        }
    }

    public CrowCastNodeIdentity? Coordinator
    {
        get
        {
            lock (_membershipGate)
            {
                return _coordinator;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _started) == 1)
            {
                return;
            }

            Volatile.Write(ref _started, 1);
            var context = new CrowCastBackendContext(LocalNode, CreateBackendSettings(), HandleNotificationAsync);

            try
            {
                await _backend.StartAsync(context, cancellationToken).ConfigureAwait(false);
                await EnsureLocalMemberAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "CrowCast node {NodeId} generation {Generation} joined cluster {ClusterName}",
                    LocalNode.NodeId,
                    LocalNode.Generation,
                    _options.ClusterName);
            }
            catch
            {
                Volatile.Write(ref _started, 0);
                lock (_membershipGate)
                {
                    _members.Remove(LocalNode);
                    _coordinator = null;
                }

                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _started) == 0)
            {
                return;
            }

            await _backend.StopAsync(cancellationToken).ConfigureAwait(false);

            CrowCastMember? current;
            lock (_membershipGate)
            {
                _members.TryGetValue(LocalNode, out current);
            }

            if (current is not null && current.Status != CrowCastMemberStatus.Left)
            {
                var now = _timeProvider.GetUtcNow();
                await UpdateMemberAsync(
                    new CrowCastMember(
                        LocalNode,
                        CrowCastMemberStatus.Left,
                        current.FirstSeenAtUtc,
                        now,
                        current.Endpoint,
                        current.Metadata),
                    cancellationToken).ConfigureAwait(false);
            }

            Volatile.Write(ref _started, 0);
            _logger.LogInformation("CrowCast node {NodeId} left cluster {ClusterName}", LocalNode.NodeId, _options.ClusterName);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask PublishAsync(CrowCastMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Volatile.Read(ref _started) == 0)
        {
            throw new InvalidOperationException("CrowCast has not been started.");
        }

        var now = _timeProvider.GetUtcNow();
        if (!CrowCastInputValidator.TryValidateMessageShape(message, _options, out var reason) ||
            CrowCastInputValidator.IsTooFarInFuture(message, _options, now) ||
            CrowCastInputValidator.IsExpired(message, _options, now))
        {
            reason = string.IsNullOrEmpty(reason)
                ? CrowCastInputValidator.IsTooFarInFuture(message, _options, now)
                    ? "timestamp_too_far_in_future"
                    : "expired"
                : reason;
            _telemetry.RecordRejectedMessage(reason);
            throw new ArgumentException($"CrowCast rejected the message: {reason}.", nameof(message));
        }

        var outbound = message.Origin is null ? message.WithOrigin(LocalNode) : message;
        await _backend.PublishAsync(outbound, cancellationToken).ConfigureAwait(false);
        _telemetry.RecordPublished();
    }

    public CrowCastDiagnosticsSnapshot GetSnapshot() => _telemetry.Snapshot(LocalNode, Coordinator);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (Volatile.Read(ref _started) == 1)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await _backend.DisposeAsync().ConfigureAwait(false);
        _telemetry.Dispose();
        _lifecycleGate.Dispose();
    }

    private CrowCastBackendSettings CreateBackendSettings()
    {
        var metadata = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(_options.Metadata, StringComparer.Ordinal));

        return new CrowCastBackendSettings(
            _options.ClusterName,
            _options.ExchangeInterval,
            _options.SuspectAfter,
            _options.DeadAfter,
            metadata,
            new CrowCastProtocolLimits(
                _options.MaximumMessageContentBytes,
                _options.MaximumMessageTypeBytes,
                _options.MaximumMetadataEntries,
                _options.MaximumMetadataKeyBytes,
                _options.MaximumMetadataValueBytes,
                _options.MaximumEndpointBytes,
                _options.MaximumClockSkew));
    }

    private async ValueTask HandleNotificationAsync(
        CrowCastBackendNotification notification,
        CancellationToken cancellationToken)
    {
        switch (notification)
        {
            case CrowCastBackendMemberChanged memberChanged:
                await UpdateMemberAsync(memberChanged.Member, cancellationToken).ConfigureAwait(false);
                break;
            case CrowCastBackendMessageReceived messageReceived:
                await ReceiveMessageAsync(messageReceived.Message, cancellationToken).ConfigureAwait(false);
                break;
            default:
                _logger.LogWarning("Ignoring unsupported CrowCast backend notification {NotificationType}", notification.GetType());
                break;
        }
    }

    private async ValueTask EnsureLocalMemberAsync(CancellationToken cancellationToken)
    {
        lock (_membershipGate)
        {
            if (_members.ContainsKey(LocalNode))
            {
                return;
            }
        }

        var now = _timeProvider.GetUtcNow();
        await UpdateMemberAsync(
            new CrowCastMember(
                LocalNode,
                CrowCastMemberStatus.Alive,
                now,
                now,
                metadata: new Dictionary<string, string>(_options.Metadata, StringComparer.Ordinal)),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask UpdateMemberAsync(CrowCastMember incoming, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (!CrowCastInputValidator.TryValidateMember(incoming, _options, now, out var rejectionReason))
        {
            _telemetry.RecordRejectedMembershipUpdate(rejectionReason);
            LogRejectedInput("membership_update", rejectionReason);
            return;
        }

        CrowCastMember? previous;
        CrowCastMember current;

        lock (_membershipGate)
        {
            _members.TryGetValue(incoming.Identity, out previous);
            current = previous is null
                ? incoming
                : new CrowCastMember(
                    incoming.Identity,
                    incoming.Status,
                    previous.FirstSeenAtUtc,
                    incoming.LastChangedAtUtc,
                    incoming.Endpoint,
                    incoming.Metadata);

            if (previous is not null && MembersEquivalent(previous, current))
            {
                return;
            }

            _members[current.Identity] = current;
        }

        _telemetry.RecordMembershipChange(current.Status);
        await DispatchAsync(
            new CrowCastMemberChanged(previous, current, _timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        await ReevaluateCoordinatorAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ReceiveMessageAsync(CrowCastMessage message, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (!CrowCastInputValidator.TryValidateMessageShape(message, _options, out var rejectionReason))
        {
            _telemetry.RecordRejectedMessage(rejectionReason);
            LogRejectedInput("message", rejectionReason);
            return;
        }

        if (CrowCastInputValidator.IsTooFarInFuture(message, _options, now))
        {
            const string reason = "timestamp_too_far_in_future";
            _telemetry.RecordRejectedMessage(reason);
            LogRejectedInput("message", reason);
            return;
        }

        if (CrowCastInputValidator.IsExpired(message, _options, now))
        {
            _telemetry.RecordExpired();
            return;
        }

        if (!_receiveRateLimiter.TryAcquire(now))
        {
            const string reason = "rate_limit";
            _telemetry.RecordRejectedMessage(reason, rateLimited: true);
            LogRejectedInput("message", reason);
            return;
        }

        if (!TryAcceptMessage(message.Id, now))
        {
            _telemetry.RecordDuplicate();
            return;
        }

        _telemetry.RecordReceived();
        await DispatchAsync(new CrowCastMessageReceived(message, now), cancellationToken).ConfigureAwait(false);
    }

    private bool TryAcceptMessage(CrowCastMessageId messageId, DateTimeOffset now)
    {
        var expiresAt = now + _options.DeduplicationWindow;
        while (true)
        {
            if (_deduplication.TryGetValue(messageId, out var existing))
            {
                if (existing > now)
                {
                    return false;
                }

                if (_deduplication.TryUpdate(messageId, expiresAt, existing))
                {
                    break;
                }

                continue;
            }

            if (_deduplication.TryAdd(messageId, expiresAt))
            {
                break;
            }
        }

        if (Interlocked.Increment(ref _messagesSinceCleanup) >= 256 ||
            _deduplication.Count > _options.MaximumDeduplicationEntries)
        {
            Interlocked.Exchange(ref _messagesSinceCleanup, 0);
            PruneDeduplication(now);
        }

        return true;
    }

    private void PruneDeduplication(DateTimeOffset now)
    {
        foreach (var entry in _deduplication)
        {
            if (entry.Value <= now)
            {
                _deduplication.TryRemove(entry.Key, out _);
            }
        }

        var overflow = _deduplication.Count - _options.MaximumDeduplicationEntries;
        if (overflow <= 0)
        {
            return;
        }

        foreach (var entry in _deduplication.OrderBy(entry => entry.Value).Take(overflow))
        {
            _deduplication.TryRemove(entry.Key, out _);
        }
    }

    private async ValueTask ReevaluateCoordinatorAsync(CancellationToken cancellationToken)
    {
        CrowCastNodeIdentity? previous;
        CrowCastNodeIdentity? current;

        lock (_membershipGate)
        {
            previous = _coordinator;
            current = _options.EnableCoordinatorElection
                ? _members.Values
                    .Where(member => member.Status == CrowCastMemberStatus.Alive)
                    .Select(member => member.Identity)
                    .OrderBy(identity => identity)
                    .FirstOrDefault()
                : null;

            if (Equals(previous, current))
            {
                return;
            }

            _coordinator = current;
        }

        _telemetry.RecordCoordinatorChange();
        await DispatchAsync(
            new CrowCastCoordinatorChanged(previous, current, _timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DispatchAsync(CrowCastEvent @event, CancellationToken cancellationToken)
    {
        foreach (var handler in _eventHandlers)
        {
            try
            {
                await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "CrowCast event handler {HandlerType} failed while processing {EventType}",
                    handler.GetType(),
                    @event.GetType());
            }
        }
    }

    private void LogRejectedInput(string inputType, string reason)
    {
        var count = Interlocked.Increment(ref _rejectionLogCount);
        if (count == 1 || count % 100 == 0)
        {
            _logger.LogWarning(
                "CrowCast rejected {InputType} input because {Reason}; rejected input count is {RejectedCount}",
                inputType,
                reason,
                count);
        }
    }

    private static bool MembersEquivalent(CrowCastMember left, CrowCastMember right) =>
        left.Identity == right.Identity &&
        left.Status == right.Status &&
        string.Equals(left.Endpoint, right.Endpoint, StringComparison.Ordinal) &&
        left.Metadata.Count == right.Metadata.Count &&
        left.Metadata.All(pair => right.Metadata.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
