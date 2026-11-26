using CrowCast.ControlPlane.Configuration;
using CrowCast.ControlPlane.Grpc;
using CrowCast.ControlPlane.Protocol.V1;
using CrowCast.ControlPlane.Storage;
using CrowCast.Observability.Models;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests.ControlPlane;

public sealed class ObservationGrpcServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Service_rejects_missing_or_unsupported_hello()
    {
        var store = Store();
        var service = new CrowCastObservationGrpcService(store, new FixedTimeProvider(Now));
        var invalidWriter = new TestWriter<ControlPlaneFrame>();
        await service.Observe(new TestReader<ObservationFrame>([new() { FrameSequence = 1 }]), invalidWriter, new TestServerCallContext());
        Assert.Equal(ControlPlaneFrame.PayloadOneofCase.Rejected, Assert.Single(invalidWriter.Items).PayloadCase);

        var versionWriter = new TestWriter<ControlPlaneFrame>();
        await service.Observe(new TestReader<ObservationFrame>([new() { FrameSequence = 1, Hello = Hello(schema: 2) }]), versionWriter, new TestServerCallContext());
        Assert.Equal(ControlPlaneFrame.PayloadOneofCase.Rejected, Assert.Single(versionWriter.Items).PayloadCase);

        var identityWriter = new TestWriter<ControlPlaneFrame>();
        await service.Observe(new TestReader<ObservationFrame>([new() { FrameSequence = 1, Hello = new Hello { SchemaVersion = 1 } }]), identityWriter, new TestServerCallContext());
        Assert.Equal(ControlPlaneFrame.PayloadOneofCase.Rejected, Assert.Single(identityWriter.Items).PayloadCase);

        foreach (var invalid in InvalidIdentities())
        {
            var writer = new TestWriter<ControlPlaneFrame>();
            await service.Observe(new TestReader<ObservationFrame>([new() { FrameSequence = 1, Hello = invalid }]), writer, new TestServerCallContext());
            Assert.Equal(ControlPlaneFrame.PayloadOneofCase.Rejected, Assert.Single(writer.Items).PayloadCase);
        }
    }

    [Fact]
    public async Task Service_processes_every_frame_and_requests_resync_for_non_monotonic_input()
    {
        var store = Store();
        var service = new CrowCastObservationGrpcService(store, new FixedTimeProvider(Now));
        var hello = Hello();
        var snapshot = Snapshot();
        var frames = new List<ObservationFrame>
        {
            new() { FrameSequence = 1, Hello = hello },
            new() { FrameSequence = 2, Snapshot = snapshot },
            new() { FrameSequence = 2, Heartbeat = new Heartbeat() },
            new() { FrameSequence = 3, Events = Events() },
            new() { FrameSequence = 4, Heartbeat = new Heartbeat() },
            new() { FrameSequence = 5, Goodbye = new Goodbye { Reason = "shutdown" } },
            new() { FrameSequence = 6, Snapshot = new Snapshot { SchemaVersion = 1, Cluster = new ClusterKey { Environment = "other" }, Observer = snapshot.Observer, Counters = new() } },
            new() { FrameSequence = 7, Hello = hello },
            new() { FrameSequence = 8 }
        };
        var writer = new TestWriter<ControlPlaneFrame>();
        await service.Observe(new TestReader<ObservationFrame>(frames), writer, new TestServerCallContext());

        Assert.Equal(ControlPlaneFrame.PayloadOneofCase.Accepted, writer.Items[0].PayloadCase);
        Assert.Contains(writer.Items, item => item.PayloadCase == ControlPlaneFrame.PayloadOneofCase.ResyncRequired);
        var key = new CrowCastClusterKey("Production", "orders", "alpha", "west");
        var view = store.GetCluster(key, Now)!;
        Assert.Single(view.ObserverViews);
        Assert.False(view.ObserverViews[0].Connected);
        Assert.Single(store.GetEvents(key));
    }

    private static InMemoryCrowCastControlPlaneStore Store() =>
        new(Options.Create(new CrowCastControlPlaneOptions()));

    private static Hello Hello(int schema = 1) => new()
    {
        SchemaVersion = schema,
        Cluster = new ClusterKey { Environment = "Production", Service = "orders", Cluster = "alpha", Scope = "west" },
        Observer = new ObserverIdentity { NodeId = "node", Generation = "generation" }
    };

    private static IEnumerable<Hello> InvalidIdentities()
    {
        var missingService = Hello();
        missingService.Cluster.Service = " ";
        yield return missingService;
        var missingCluster = Hello();
        missingCluster.Cluster.Cluster = " ";
        yield return missingCluster;
        var missingNode = Hello();
        missingNode.Observer.NodeId = " ";
        yield return missingNode;
        var missingGeneration = Hello();
        missingGeneration.Observer.Generation = " ";
        yield return missingGeneration;
    }

    private static Snapshot Snapshot()
    {
        var value = new Snapshot
        {
            SchemaVersion = 1,
            Cluster = Hello().Cluster,
            Observer = Hello().Observer,
            ObservationSequence = 1,
            ObservedUnixMs = Now.ToUnixTimeMilliseconds(),
            Coordinator = Hello().Observer,
            Counters = new TelemetryCounters()
        };
        value.Members.Add(new MemberObservation
        {
            Identity = Hello().Observer,
            Status = "Alive",
            FirstSeenUnixMs = Now.ToUnixTimeMilliseconds(),
            LastChangedUnixMs = Now.ToUnixTimeMilliseconds(),
            IsCoordinator = true,
            IsLocal = true
        });
        return value;
    }

    private static EventBatch Events()
    {
        var value = new EventBatch();
        value.Events.Add(new ObservationEvent
        {
            Sequence = 1,
            OccurredUnixMs = Now.ToUnixTimeMilliseconds(),
            Category = "membership",
            Summary = "joined"
        });
        return value;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestReader<T>(IReadOnlyList<T> items) : IAsyncStreamReader<T>
    {
        private int _index = -1;
        public T Current => items[_index];
        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _index++;
            return Task.FromResult(_index < items.Count);
        }
    }

    private sealed class TestWriter<T> : IServerStreamWriter<T>
    {
        public List<T> Items { get; } = [];
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message)
        {
            Items.Add(message);
            return Task.CompletedTask;
        }
        public Task WriteAsync(T message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        private readonly Metadata _trailers = [];
        private Status _status;
        private WriteOptions? _writeOptions;
        protected override string MethodCore => "Observe";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:1";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => _trailers;
        protected override Status StatusCore { get => _status; set => _status = value; }
        protected override WriteOptions? WriteOptionsCore { get => _writeOptions; set => _writeOptions = value; }
        protected override AuthContext AuthContextCore { get; } = new("anonymous", []);
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
