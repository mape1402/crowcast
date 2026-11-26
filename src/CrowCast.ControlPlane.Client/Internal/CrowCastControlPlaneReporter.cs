using System.Reflection;
using System.Threading.Channels;
using CrowCast.ControlPlane.Client.Configuration;
using CrowCast.ControlPlane.Protocol.V1;
using CrowCast.Observability;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrowCast.ControlPlane.Client.Internal;

internal sealed class CrowCastControlPlaneReporter : BackgroundService
{
    private readonly ICrowCastObservationSource _source;
    private readonly CrowCastControlPlaneClientOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CrowCastControlPlaneReporter> _logger;
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private long _frameSequence;
    private long _lastEventSequence;
    private int _resyncRequired = 1;

    public CrowCastControlPlaneReporter(
        ICrowCastObservationSource source,
        IOptions<CrowCastControlPlaneClientOptions> options,
        TimeProvider timeProvider,
        ILogger<CrowCastControlPlaneReporter> logger)
    {
        _source = source;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _source.Changed += OnChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = _options.MinimumReconnectDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(stoppingToken).ConfigureAwait(false);
                delay = _options.MinimumReconnectDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "CrowCast control plane stream disconnected; retrying in {Delay}", delay);
                await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250)), _timeProvider, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, _options.MaximumReconnectDelay.TotalMilliseconds));
            }
        }
    }

    public override void Dispose()
    {
        _source.Changed -= OnChanged;
        base.Dispose();
    }

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        using var channel = GrpcChannel.ForAddress(_options.Endpoint!);
        var client = new CrowCastObservationService.CrowCastObservationServiceClient(channel);
        using var call = client.Observe(cancellationToken: cancellationToken);
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var responses = ReadResponsesAsync(call.ResponseStream, sessionCancellation.Token);
        var snapshot = _source.GetSnapshot();
        await call.RequestStream.WriteAsync(new ObservationFrame
        {
            FrameSequence = NextFrame(),
            Hello = new Hello
            {
                SchemaVersion = snapshot.SchemaVersion,
                Cluster = CrowCastProtocolMapper.Map(snapshot.Cluster),
                Observer = CrowCastProtocolMapper.Map(snapshot.Observer),
                LibraryVersion = Assembly.GetAssembly(typeof(ICrowCast))?.GetName().Version?.ToString() ?? "unknown"
            }
        }, cancellationToken).ConfigureAwait(false);

        var nextSnapshot = DateTimeOffset.MinValue;
        var nextHeartbeat = DateTimeOffset.MinValue;
        try
        {
            while (!sessionCancellation.IsCancellationRequested)
            {
                var now = _timeProvider.GetUtcNow();
                if (Interlocked.Exchange(ref _resyncRequired, 0) == 1 || now >= nextSnapshot)
                {
                    snapshot = _source.GetSnapshot();
                    await call.RequestStream.WriteAsync(new ObservationFrame
                    {
                        FrameSequence = NextFrame(),
                        Snapshot = CrowCastProtocolMapper.Map(snapshot)
                    }, cancellationToken).ConfigureAwait(false);
                    nextSnapshot = now + _options.SnapshotInterval;
                    nextHeartbeat = now + _options.HeartbeatInterval;
                }

                var events = _source.GetEvents(_lastEventSequence, _options.MaximumBatchSize).OrderBy(item => item.Sequence).ToArray();
                if (events.Length > 0)
                {
                    var batch = new EventBatch();
                    batch.Events.AddRange(events.Select(CrowCastProtocolMapper.Map));
                    await call.RequestStream.WriteAsync(new ObservationFrame
                    {
                        FrameSequence = NextFrame(),
                        Events = batch
                    }, cancellationToken).ConfigureAwait(false);
                    _lastEventSequence = events[^1].Sequence;
                    nextHeartbeat = now + _options.HeartbeatInterval;
                }
                else if (now >= nextHeartbeat)
                {
                    await call.RequestStream.WriteAsync(new ObservationFrame
                    {
                        FrameSequence = NextFrame(),
                        Heartbeat = new Heartbeat { ObservedUnixMs = now.ToUnixTimeMilliseconds() }
                    }, cancellationToken).ConfigureAwait(false);
                    nextHeartbeat = now + _options.HeartbeatInterval;
                }

                var wait = _wake.Reader.WaitToReadAsync(sessionCancellation.Token).AsTask();
                var timer = Task.Delay(TimeSpan.FromMilliseconds(500), _timeProvider, sessionCancellation.Token);
                await Task.WhenAny(wait, timer).ConfigureAwait(false);
                while (_wake.Reader.TryRead(out _)) { }
            }
        }
        finally
        {
            sessionCancellation.Cancel();
            if (!cancellationToken.IsCancellationRequested)
            {
                try { await call.RequestStream.CompleteAsync().ConfigureAwait(false); } catch (RpcException) { }
            }
            try { await responses.ConfigureAwait(false); } catch (OperationCanceledException) { } catch (RpcException) { }
        }
    }

    private async Task ReadResponsesAsync(IAsyncStreamReader<ControlPlaneFrame> responses, CancellationToken cancellationToken)
    {
        while (await responses.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            var response = responses.Current;
            if (response.PayloadCase == ControlPlaneFrame.PayloadOneofCase.ResyncRequired)
            {
                Interlocked.Exchange(ref _resyncRequired, 1);
                OnChanged();
            }
            else if (response.PayloadCase == ControlPlaneFrame.PayloadOneofCase.Rejected)
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, response.Rejected.Reason));
            }
        }
    }

    private long NextFrame() => Interlocked.Increment(ref _frameSequence);

    private void OnChanged() => _wake.Writer.TryWrite(true);
}
