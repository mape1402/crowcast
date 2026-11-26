using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using CrowCast.Akka.Transport;
using CrowCast.Backends;
using CrowCast.Membership;
using CrowCast.Messaging;
using Microsoft.Extensions.Logging;

namespace CrowCast.Akka.Internal;

internal sealed class CrowCastClusterActor : ReceiveActor
{
    private readonly global::Akka.Cluster.Cluster _cluster;
    private readonly IActorRef _mediator;
    private readonly CrowCastBackendContext _backendContext;
    private readonly TaskCompletionSource _memberReady;
    private readonly TaskCompletionSource _subscriptionReady;
    private readonly ILogger _logger;
    private readonly string _topic;
    private readonly string _memberTopic;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, CrowCastNodeIdentity> _identitiesByEndpoint = new(StringComparer.Ordinal);
    private readonly Dictionary<CrowCastNodeIdentity, CrowCastNodeAnnouncement> _announcements = [];
    private readonly Dictionary<string, CrowCastMemberStatus> _statusByEndpoint = new(StringComparer.Ordinal);
    private readonly Dictionary<CrowCastNodeIdentity, CrowCastMemberStatus> _statusByIdentity = [];
    private readonly Dictionary<CrowCastNodeIdentity, long> _terminalIdentityOrder = [];
    private readonly HashSet<string> _pendingSubscriptions;
    private ICancelable _announcementSchedule = Cancelable.CreateCanceled();
    private long _rejectedWireInputCount;
    private long _terminalIdentitySequence;

    public CrowCastClusterActor(
        CrowCastBackendContext backendContext,
        TaskCompletionSource memberReady,
        TaskCompletionSource subscriptionReady,
        ILogger logger,
        DateTimeOffset startedAtUtc,
        TimeProvider timeProvider)
    {
        _backendContext = backendContext;
        _memberReady = memberReady;
        _subscriptionReady = subscriptionReady;
        _logger = logger;
        _startedAtUtc = startedAtUtc;
        _timeProvider = timeProvider;
        _topic = $"crowcast-{backendContext.Settings.ClusterName}";
        _memberTopic = GetMemberTopic(_topic, backendContext.LocalNode);
        _pendingSubscriptions = [_topic, _memberTopic];
        _cluster = global::Akka.Cluster.Cluster.Get(Context.System);
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        Receive<SubscribeAck>(HandleSubscribeAck);
        Receive<PublishAnnouncement>(_ => PublishLocalAnnouncement());
        Receive<PublishMessage>(message =>
        {
            var topic = message.Target.Member is null
                ? _topic
                : GetMemberTopic(_topic, message.Target.Member);
            _mediator.Tell(new Publish(topic, CrowCastWireMapper.ToEnvelope(message.Message)));
        });
        Receive<PrepareToLeave>(HandlePrepareToLeave);

        ReceiveAsync<global::Akka.Cluster.ClusterEvent.CurrentClusterState>(HandleCurrentStateAsync);
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberUp>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Alive));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.ReachableMember>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Alive));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.UnreachableMember>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Suspect));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberLeft>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Left));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberExited>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Left));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberDowned>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Dead));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberRemoved>(HandleMemberRemovedAsync);

        ReceiveAsync<CrowCastNodeAnnouncement>(HandleAnnouncementAsync);
        ReceiveAsync<CrowCastNodeLeaving>(HandleLeavingAsync);
        ReceiveAsync<CrowCastMessageEnvelope>(HandleMessageAsync);
    }

    protected override void PreStart()
    {
        _cluster.Subscribe(
            Self,
            global::Akka.Cluster.ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot,
            typeof(global::Akka.Cluster.ClusterEvent.IMemberEvent),
            typeof(global::Akka.Cluster.ClusterEvent.IReachabilityEvent));
        _mediator.Tell(new Subscribe(_topic, Self));
        _mediator.Tell(new Subscribe(_memberTopic, Self));
        _announcementSchedule = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            TimeSpan.Zero,
            _backendContext.Settings.ExchangeInterval,
            Self,
            PublishAnnouncement.Instance,
            Self);
        base.PreStart();
    }

    protected override void PostStop()
    {
        _announcementSchedule.Cancel();
        _mediator.Tell(new Unsubscribe(_topic, Self));
        _mediator.Tell(new Unsubscribe(_memberTopic, Self));
        _cluster.Unsubscribe(Self);
        base.PostStop();
    }

    private void HandleSubscribeAck(SubscribeAck message)
    {
        if (!_pendingSubscriptions.Remove(message.Subscribe.Topic))
        {
            return;
        }

        if (message.Subscribe.Topic == _topic)
        {
            PublishLocalAnnouncement();
        }

        if (_pendingSubscriptions.Count == 0)
        {
            _subscriptionReady.TrySetResult();
        }
    }

    private void PublishLocalAnnouncement()
    {
        var identity = _backendContext.LocalNode;
        var announcement = new CrowCastNodeAnnouncement(
            identity.NodeId.Value,
            identity.Generation.Value,
            _cluster.SelfAddress.ToString(),
            _startedAtUtc,
            _backendContext.Settings.Metadata);
        _mediator.Tell(new Publish(_topic, announcement));
    }

    private void HandlePrepareToLeave(PrepareToLeave message)
    {
        var identity = _backendContext.LocalNode;
        _mediator.Tell(new Publish(
            _topic,
            new CrowCastNodeLeaving(
                identity.NodeId.Value,
                identity.Generation.Value,
                _cluster.SelfAddress.ToString(),
                _timeProvider.GetUtcNow(),
                _backendContext.Settings.Metadata)));
        message.Completion.TrySetResult();
    }

    private async Task HandleCurrentStateAsync(global::Akka.Cluster.ClusterEvent.CurrentClusterState state)
    {
        foreach (var member in state.Members)
        {
            var status = member.Status switch
            {
                global::Akka.Cluster.MemberStatus.Up or global::Akka.Cluster.MemberStatus.WeaklyUp => CrowCastMemberStatus.Alive,
                global::Akka.Cluster.MemberStatus.Leaving or global::Akka.Cluster.MemberStatus.Exiting => CrowCastMemberStatus.Left,
                global::Akka.Cluster.MemberStatus.Down or global::Akka.Cluster.MemberStatus.Removed => CrowCastMemberStatus.Dead,
                _ => CrowCastMemberStatus.Suspect
            };
            await UpdateClusterStatusAsync(member, status).ConfigureAwait(false);
        }

        foreach (var member in state.Unreachable)
        {
            await UpdateClusterStatusAsync(member, CrowCastMemberStatus.Suspect).ConfigureAwait(false);
        }
    }

    private async Task HandleMemberRemovedAsync(global::Akka.Cluster.ClusterEvent.MemberRemoved message)
    {
        var status = message.PreviousStatus is global::Akka.Cluster.MemberStatus.Leaving or global::Akka.Cluster.MemberStatus.Exiting
            ? CrowCastMemberStatus.Left
            : CrowCastMemberStatus.Dead;
        var endpoint = message.Member.Address.ToString();
        _identitiesByEndpoint.TryGetValue(endpoint, out var identity);

        await UpdateClusterStatusAsync(message.Member, status).ConfigureAwait(false);

        if (identity is null)
        {
            _statusByEndpoint.Remove(endpoint);
            return;
        }

        CompactTerminalIdentity(endpoint, identity, status);
    }

    private async Task HandleAnnouncementAsync(CrowCastNodeAnnouncement announcement)
    {
        if (!CrowCastWireValidator.TryValidate(
                announcement,
                _backendContext.Settings.Limits,
                _timeProvider.GetUtcNow(),
                out var rejectionReason))
        {
            LogRejectedWireInput("node_announcement", rejectionReason);
            return;
        }

        var identity = new CrowCastNodeIdentity(
            new CrowCastNodeId(announcement.NodeId),
            new CrowCastNodeGeneration(announcement.Generation));

        if (_statusByIdentity.TryGetValue(identity, out var identityStatus) &&
            identityStatus is CrowCastMemberStatus.Dead or CrowCastMemberStatus.Left)
        {
            await NotifyMemberAsync(identity, announcement, identityStatus).ConfigureAwait(false);
            return;
        }

        _identitiesByEndpoint.TryGetValue(announcement.Endpoint, out var previousIdentity);
        if (previousIdentity is not null &&
            previousIdentity != identity &&
            _announcements.TryGetValue(previousIdentity, out var previousAnnouncement) &&
            previousAnnouncement.StartedAtUtc > announcement.StartedAtUtc)
        {
            return;
        }

        if (previousIdentity is not null &&
            previousIdentity != identity &&
            _announcements.TryGetValue(previousIdentity, out previousAnnouncement))
        {
            await NotifyMemberAsync(
                    previousIdentity,
                    previousAnnouncement,
                    CrowCastMemberStatus.Dead)
                .ConfigureAwait(false);
            CompactTerminalIdentity(
                announcement.Endpoint,
                previousIdentity,
                CrowCastMemberStatus.Dead);
        }

        _identitiesByEndpoint[announcement.Endpoint] = identity;
        _announcements[identity] = announcement;

        // Terminal identities returned above; a new generation at the same endpoint is alive.
        const CrowCastMemberStatus status = CrowCastMemberStatus.Alive;
        _statusByEndpoint[announcement.Endpoint] = status;

        _statusByIdentity[identity] = status;

        await NotifyMemberAsync(identity, announcement, status).ConfigureAwait(false);
    }

    private async Task HandleLeavingAsync(CrowCastNodeLeaving leaving)
    {
        if (!CrowCastWireValidator.TryValidate(
                leaving,
                _backendContext.Settings.Limits,
                _timeProvider.GetUtcNow(),
                out var rejectionReason))
        {
            LogRejectedWireInput("node_leaving", rejectionReason);
            return;
        }

        var identity = new CrowCastNodeIdentity(
            new CrowCastNodeId(leaving.NodeId),
            new CrowCastNodeGeneration(leaving.Generation));
        var announcement = new CrowCastNodeAnnouncement(
            leaving.NodeId,
            leaving.Generation,
            leaving.Endpoint,
            leaving.LeftAtUtc,
            leaving.Metadata);

        await NotifyMemberAsync(identity, announcement, CrowCastMemberStatus.Left).ConfigureAwait(false);
        CompactTerminalIdentity(leaving.Endpoint, identity, CrowCastMemberStatus.Left);
    }

    private Task HandleMessageAsync(CrowCastMessageEnvelope envelope)
    {
        if (!CrowCastWireValidator.TryValidate(envelope, out var rejectionReason))
        {
            LogRejectedWireInput("message", rejectionReason);
            return Task.CompletedTask;
        }

        return _backendContext.NotifyAsync(
            new CrowCastBackendMessageReceived(CrowCastWireMapper.ToMessage(envelope)),
            CancellationToken.None).AsTask();
    }

    private Task UpdateClusterStatusAsync(global::Akka.Cluster.Member member, CrowCastMemberStatus status)
    {
        if (member.Address == _cluster.SelfAddress && status == CrowCastMemberStatus.Alive)
        {
            PublishLocalAnnouncement();
            _memberReady.TrySetResult();
        }

        var endpoint = member.Address.ToString();
        if (_statusByEndpoint.TryGetValue(endpoint, out var currentStatus) &&
            currentStatus is CrowCastMemberStatus.Dead or CrowCastMemberStatus.Left)
        {
            status = currentStatus;
        }

        _statusByEndpoint[endpoint] = status;

        if (!_identitiesByEndpoint.TryGetValue(endpoint, out var identity) ||
            !_announcements.TryGetValue(identity, out var announcement))
        {
            return Task.CompletedTask;
        }

        if (_statusByIdentity.TryGetValue(identity, out var identityStatus) &&
            identityStatus is CrowCastMemberStatus.Dead or CrowCastMemberStatus.Left)
        {
            status = identityStatus;
        }

        _statusByIdentity[identity] = status;

        return NotifyMemberAsync(identity, announcement, status);
    }

    private void CompactTerminalIdentity(
        string endpoint,
        CrowCastNodeIdentity identity,
        CrowCastMemberStatus status)
    {
        if (!_identitiesByEndpoint.TryGetValue(endpoint, out var mappedIdentity) ||
            mappedIdentity == identity)
        {
            _identitiesByEndpoint.Remove(endpoint);
            _statusByEndpoint.Remove(endpoint);
        }

        _announcements.Remove(identity);
        _statusByIdentity[identity] = status;
        _terminalIdentityOrder[identity] = ++_terminalIdentitySequence;
        PruneTerminalIdentities();
    }

    private void PruneTerminalIdentities()
    {
        var overflow = _terminalIdentityOrder.Count -
            _backendContext.Settings.MaximumRetainedTerminalMembers;
        if (overflow <= 0)
        {
            return;
        }

        var identities = _terminalIdentityOrder
            .OrderBy(pair => pair.Value)
            .Take(overflow)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var identity in identities)
        {
            _terminalIdentityOrder.Remove(identity);
            _statusByIdentity.Remove(identity);
        }
    }

    private Task NotifyMemberAsync(
        CrowCastNodeIdentity identity,
        CrowCastNodeAnnouncement announcement,
        CrowCastMemberStatus status)
    {
        _logger.LogDebug(
            "CrowCast observed node {NodeId} generation {Generation} as {Status}",
            identity.NodeId,
            identity.Generation,
            status);

        var member = new CrowCastMember(
            identity,
            status,
            announcement.StartedAtUtc,
            _timeProvider.GetUtcNow(),
            announcement.Endpoint,
            announcement.Metadata);
        return _backendContext.NotifyAsync(
            new CrowCastBackendMemberChanged(member),
            CancellationToken.None).AsTask();
    }

    private void LogRejectedWireInput(string inputType, string reason)
    {
        var count = ++_rejectedWireInputCount;
        if (count == 1 || count % 100 == 0)
        {
            _logger.LogWarning(
                "CrowCast rejected {InputType} wire input because {Reason}; rejected wire input count is {RejectedCount}",
                inputType,
                reason,
                count);
        }
    }

    private static string GetMemberTopic(string clusterTopic, CrowCastNodeIdentity member) =>
        $"{clusterTopic}-member-{member.NodeId.Value:N}-{member.Generation.Value:N}";

    internal sealed record PublishMessage(CrowCastMessage Message, CrowCastMessageTarget Target);

    internal sealed record PrepareToLeave(TaskCompletionSource Completion);

    private sealed class PublishAnnouncement
    {
        public static PublishAnnouncement Instance { get; } = new();
    }
}
