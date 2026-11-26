using CrowCast.ControlPlane.Client.Internal;
using CrowCast.Observability.Models;

namespace CrowCast.Tests.ControlPlane;

public sealed class ProtocolMapperTests
{
    [Fact]
    public void Client_and_server_mappers_round_trip_snapshot_and_event()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_123);
        var cluster = new CrowCastClusterKey("Production", "orders", "alpha", "west");
        var observer = new CrowCastObserverIdentity("node", "generation");
        var snapshot = new CrowCastObservationSnapshot(
            1,
            cluster,
            observer,
            42,
            now,
            observer,
            [new(observer, "Alive", now.AddMinutes(-1), now, "akka://node", new Dictionary<string, string> { ["zone"] = "a" }, true, true)],
            new(1, 2, 3, 4, 5, 6, 7, 8, 9));
        var protocol = CrowCastProtocolMapper.Map(snapshot);
        var result = CrowCast.ControlPlane.Grpc.CrowCastProtocolMapper.Map(protocol);
        Assert.Equal(snapshot.SchemaVersion, result.SchemaVersion);
        Assert.Equal(snapshot.Cluster, result.Cluster);
        Assert.Equal(snapshot.Observer, result.Observer);
        Assert.Equal(snapshot.Sequence, result.Sequence);
        Assert.Equal(snapshot.ObservedAtUtc, result.ObservedAtUtc);
        Assert.Equal(snapshot.Coordinator, result.Coordinator);
        Assert.Single(result.Members);
        Assert.Equal(snapshot.Members[0].Identity, result.Members[0].Identity);
        Assert.Equal(snapshot.Members[0].Status, result.Members[0].Status);
        Assert.Equal(snapshot.Members[0].FirstSeenAtUtc, result.Members[0].FirstSeenAtUtc);
        Assert.Equal(snapshot.Members[0].LastChangedAtUtc, result.Members[0].LastChangedAtUtc);
        Assert.Equal(snapshot.Members[0].Endpoint, result.Members[0].Endpoint);
        Assert.Equal(snapshot.Members[0].Metadata, result.Members[0].Metadata);
        Assert.Equal(snapshot.Members[0].IsCoordinator, result.Members[0].IsCoordinator);
        Assert.Equal(snapshot.Members[0].IsLocal, result.Members[0].IsLocal);
        Assert.Equal(snapshot.Counters, result.Counters);

        var observation = new CrowCastObservationEvent(7, now, "message", "received", observer, "Old", "New", "order.created");
        var eventResult = CrowCast.ControlPlane.Grpc.CrowCastProtocolMapper.Map(CrowCastProtocolMapper.Map(observation));
        Assert.Equal(observation, eventResult);
    }

    [Fact]
    public void Mapper_preserves_absent_optional_values()
    {
        var value = new CrowCastObservationEvent(1, DateTimeOffset.UnixEpoch, "coordination", "cleared");
        var result = CrowCast.ControlPlane.Grpc.CrowCastProtocolMapper.Map(CrowCastProtocolMapper.Map(value));
        Assert.Null(result.Subject);
        Assert.Null(result.PreviousStatus);
        Assert.Null(result.CurrentStatus);
        Assert.Null(result.MessageType);
    }
}
