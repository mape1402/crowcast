using CrowCast.Configuration;
using CrowCast.Diagnostics;
using CrowCast.Events;
using CrowCast.Membership;
using CrowCast.Messaging;
using CrowCast.Observability.Configuration;
using CrowCast.Observability.Internal;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests.Observability;

public sealed class ObservationSourceTests
{
    [Fact]
    public async Task Source_sanitizes_snapshots_records_events_and_samples()
    {
        var local = CrowCastNodeIdentity.New();
        var remote = CrowCastNodeIdentity.New();
        var now = DateTimeOffset.UtcNow;
        var member = new CrowCastMember(remote, CrowCastMemberStatus.Alive, now.AddMinutes(-1), now, "akka://remote", new Dictionary<string, string>
        {
            ["allowed"] = "yes",
            ["secret"] = "no"
        });
        var crowCast = new StubCrowCast(local, [member], remote);
        var diagnostics = new StubDiagnostics(new(local, remote, 1, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9));
        var settings = new CrowCastObservabilityOptions
        {
            ServiceName = "orders",
            EnvironmentName = "Test",
            Scope = "west",
            SamplingInterval = TimeSpan.FromMilliseconds(10),
            MaximumEvents = 2,
            MaximumSamples = 2,
            IncludeEndpoints = false
        };
        settings.IncludedMetadataKeys.Add("allowed");
        var source = new CrowCastObservationSource(
            crowCast,
            diagnostics,
            Options.Create(new CrowCastOptions { ClusterName = "alpha" }),
            Options.Create(settings),
            new StubEnvironment(),
            TimeProvider.System,
            new CrowCastObservationEventSink(Options.Create(settings)));
        var changes = 0;
        source.Changed += () => changes++;

        var snapshot = source.GetSnapshot();
        Assert.Equal("Test/west/orders/alpha", snapshot.Cluster.ToString());
        Assert.Equal(remote.NodeId.ToString(), snapshot.Coordinator!.NodeId);
        Assert.Null(snapshot.Members[0].Endpoint);
        Assert.Equal("yes", snapshot.Members[0].Metadata["allowed"]);
        Assert.False(snapshot.Members[0].IsLocal);
        Assert.True(snapshot.Members[0].IsCoordinator);

        await source.HandleAsync(new CrowCastMemberChanged(null, member, now), CancellationToken.None);
        await source.HandleAsync(new CrowCastCoordinatorChanged(local, remote, now), CancellationToken.None);
        await source.HandleAsync(new CrowCastMessageReceived(new CrowCastMessage(CrowCastMessageId.New(), "order.created", new byte[] { 1 }, now, origin: remote), now), CancellationToken.None);
        await source.HandleAsync(new UnknownEvent(now), CancellationToken.None);
        Assert.Equal(2, source.GetEvents(take: 100).Count);
        Assert.Single(source.GetEvents(afterSequence: 2));
        Assert.Equal("message", source.GetEvents()[0].Category);

        await source.StartAsync(CancellationToken.None);
        for (var index = 0; index < 50 && source.GetSamples(100).Count < 2; index++)
        {
            await Task.Delay(10);
        }
        await source.StopAsync(CancellationToken.None);
        Assert.Equal(2, source.GetSamples(100).Count);
        Assert.True(changes >= 4);
        source.Dispose();
    }

    [Fact]
    public async Task Source_uses_host_fallbacks_and_can_include_endpoints()
    {
        var local = CrowCastNodeIdentity.New();
        var now = DateTimeOffset.UtcNow;
        var member = new CrowCastMember(local, CrowCastMemberStatus.Alive, now, now, "akka://local");
        var settings = new CrowCastObservabilityOptions { IncludeEndpoints = true };
        var source = new CrowCastObservationSource(
            new StubCrowCast(local, [member], null),
            new StubDiagnostics(new(local, null, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)),
            Options.Create(new CrowCastOptions { ClusterName = " " }),
            Options.Create(settings),
            new StubEnvironment(),
            TimeProvider.System,
            new CrowCastObservationEventSink(Options.Create(settings)));
        var snapshot = source.GetSnapshot();
        Assert.Equal("Development", snapshot.Cluster.Environment);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Cluster.Service));
        Assert.Equal("crowcast", snapshot.Cluster.Cluster);
        Assert.Equal("akka://local", snapshot.Members[0].Endpoint);
        Assert.True(snapshot.Members[0].IsLocal);
        Assert.Null(snapshot.Coordinator);
        await source.HandleAsync(new CrowCastMessageReceived(CrowCastMessage.Create("anonymous", ReadOnlyMemory<byte>.Empty), now), CancellationToken.None);
        Assert.Equal("message", Assert.Single(source.GetEvents()).Category);
    }

    [Fact]
    public async Task Cancelled_event_dispatch_is_rejected()
    {
        var local = CrowCastNodeIdentity.New();
        var settings = new CrowCastObservabilityOptions();
        var source = new CrowCastObservationSource(
            new StubCrowCast(local, [], null),
            new StubDiagnostics(new(local, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)),
            Options.Create(new CrowCastOptions()),
            Options.Create(settings),
            new StubEnvironment(),
            TimeProvider.System,
            new CrowCastObservationEventSink(Options.Create(settings)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await source.HandleAsync(new UnknownEvent(DateTimeOffset.UtcNow), new CancellationToken(canceled: true)));
    }

    private sealed class StubCrowCast(
        CrowCastNodeIdentity local,
        IReadOnlyCollection<CrowCastMember> members,
        CrowCastNodeIdentity? coordinator) : ICrowCast
    {
        public CrowCastNodeIdentity LocalNode => local;
        public IReadOnlyCollection<CrowCastMember> Members => members;
        public CrowCastNodeIdentity? Coordinator => coordinator;
        public ValueTask PublishAsync(CrowCastMessage message, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask PublishToAllAsync(CrowCastMessage message, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask PublishToMemberAsync(CrowCastMessage message, CrowCastNodeIdentity member, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask PublishToCoordinatorAsync(CrowCastMessage message, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubDiagnostics(CrowCastDiagnosticsSnapshot snapshot) : ICrowCastDiagnostics
    {
        public CrowCastDiagnosticsSnapshot GetSnapshot() => snapshot;
    }

    private sealed record UnknownEvent(DateTimeOffset Time) : CrowCastEvent(Time);

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "CrowCast.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
