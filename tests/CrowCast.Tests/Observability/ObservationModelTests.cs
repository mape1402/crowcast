using CrowCast.Diagnostics;
using CrowCast.Events;
using CrowCast.Membership;
using CrowCast.Messaging;
using CrowCast.Observability.Configuration;
using CrowCast.Observability.Internal;
using CrowCast.Observability.Models;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests.Observability;

public sealed class ObservationModelTests
{
    [Fact]
    public void Cluster_key_formats_scoped_and_unscoped_values()
    {
        Assert.Equal("Production/api/crowcast", new CrowCastClusterKey("Production", "api", "crowcast", "").ToString());
        Assert.Equal("Production/west/api/crowcast", new CrowCastClusterKey("Production", "api", "crowcast", "west").ToString());
    }

    [Fact]
    public void Observer_identity_maps_core_identity()
    {
        var identity = CrowCastNodeIdentity.New();
        var result = CrowCastObserverIdentity.From(identity);
        Assert.Equal(identity.NodeId.ToString(), result.NodeId);
        Assert.Equal(identity.Generation.ToString(), result.Generation);
        Assert.Equal($"{result.NodeId}/{result.Generation}", result.ToString());
        Assert.Throws<ArgumentNullException>(() => CrowCastObserverIdentity.From(null!));
    }

    [Fact]
    public void Telemetry_counters_map_diagnostics()
    {
        var identity = CrowCastNodeIdentity.New();
        var snapshot = new CrowCastDiagnosticsSnapshot(identity, null, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);
        var result = CrowCastTelemetryCounters.From(snapshot);
        Assert.Equal(new CrowCastTelemetryCounters(5, 6, 7, 8, 9, 10, 11, 12, 13), result);
        Assert.Throws<ArgumentNullException>(() => CrowCastTelemetryCounters.From(null!));
    }

    [Fact]
    public void Bounded_buffer_keeps_newest_items_and_normalizes_capacity()
    {
        var buffer = new BoundedObservationBuffer<int>(2);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        Assert.Equal([2, 3], buffer.Snapshot());
        var minimum = new BoundedObservationBuffer<int>(0);
        minimum.Add(1);
        minimum.Add(2);
        Assert.Equal([2], minimum.Snapshot());
    }

    [Fact]
    public async Task Event_sink_handles_anonymous_messages_and_cleared_coordinator_without_subscribers()
    {
        var sink = new CrowCastObservationEventSink(Options.Create(new CrowCastObservabilityOptions()));
        var handler = new CrowCastObservationEventHandler(sink);
        var now = DateTimeOffset.UtcNow;
        await handler.HandleAsync(new CrowCastMessageReceived(CrowCastMessage.Create("anonymous", ReadOnlyMemory<byte>.Empty), now), CancellationToken.None);
        await handler.HandleAsync(new CrowCastCoordinatorChanged(CrowCastNodeIdentity.New(), null, now), CancellationToken.None);

        Assert.Equal(["coordination", "message"], sink.GetEvents(0, 10).Select(item => item.Category));
        Assert.All(sink.GetEvents(0, 10), item => Assert.Null(item.Subject));
    }
}
