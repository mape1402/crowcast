using CrowCast.ControlPlane.Configuration;
using CrowCast.ControlPlane.Storage;
using CrowCast.Observability.Models;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests.ControlPlane;

public sealed class ControlPlaneStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly CrowCastClusterKey Cluster = new("Production", "orders", "alpha", "west");

    [Fact]
    public void Store_tracks_connections_snapshots_and_health()
    {
        var store = CreateStore();
        var observer = Identity("a");
        CrowCastClusterKey? changed = null;
        store.Changed += value => changed = value;
        Assert.Null(store.GetCluster(Cluster, Now));
        store.Connect(Cluster, observer, Now);
        Assert.Equal(Cluster, changed);
        Assert.Equal("Offline", store.GetCluster(Cluster, Now)!.Health);

        store.Upsert(Snapshot(observer, Identity("member"), "Alive", observer), Now);
        var healthy = store.GetCluster(Cluster, Now)!;
        Assert.Equal("Healthy", healthy.Health);
        Assert.Equal(1, healthy.Observers);
        Assert.Equal(1, healthy.FreshObservers);
        Assert.Equal(1, healthy.AliveMembers);
        Assert.Equal(0, healthy.SuspectMembers);
        Assert.Equal(1, healthy.CoordinatorCount);
        Assert.False(healthy.MembershipDisagreement);
        Assert.Single(store.GetClusters(Now));

        store.Upsert(Snapshot(observer, Identity("member"), "Suspect", observer), Now);
        Assert.Equal("Degraded", store.GetCluster(Cluster, Now)!.Health);
        store.Upsert(new CrowCastObservationSnapshot(
            1,
            Cluster,
            observer,
            2,
            Now,
            observer,
            [
                Member("alive", "Alive"),
                Member("suspect", "Suspect"),
                Member("dead", "Dead"),
                Member("left", "Left")
            ],
            new(0, 0, 0, 0, 0, 0, 0, 0, 0)), Now);
        var everyStatus = store.GetCluster(Cluster, Now)!;
        Assert.Equal((1, 1, 1, 1), (everyStatus.AliveMembers, everyStatus.SuspectMembers, everyStatus.DeadMembers, everyStatus.LeftMembers));
        store.Disconnect(Cluster, observer, Now);
        Assert.Equal("Offline", store.GetCluster(Cluster, Now)!.Health);
        store.Touch(Cluster, observer, Now.AddSeconds(1));
        Assert.Equal(Now.AddSeconds(1), store.GetCluster(Cluster, Now.AddSeconds(1))!.LastSeenAtUtc);
    }

    [Fact]
    public void Store_detects_membership_and_coordinator_disagreement()
    {
        var store = CreateStore();
        var first = Identity("a");
        var second = Identity("b");
        store.Upsert(Snapshot(first, Identity("m1"), "Alive", first), Now);
        store.Upsert(Snapshot(second, Identity("m2"), "Alive", first), Now);
        var partition = store.GetCluster(Cluster, Now)!;
        Assert.Equal("PartitionSuspected", partition.Health);
        Assert.True(partition.MembershipDisagreement);

        store.Upsert(Snapshot(second, Identity("m2"), "Alive", second), Now);
        Assert.Equal("CoordinatorDisagreement", store.GetCluster(Cluster, Now)!.Health);

        store.Disconnect(Cluster, second, Now);
        Assert.Equal("Degraded", store.GetCluster(Cluster, Now)!.Health);
        Assert.Equal("Offline", store.GetCluster(Cluster, Now.AddMinutes(3))!.Health);
    }

    [Fact]
    public void Store_bounds_observers_and_events_and_deduplicates_per_observer()
    {
        var store = CreateStore(maxEvents: 2, maxObservers: 1);
        var first = Identity("a");
        var second = Identity("b");
        store.Connect(Cluster, first, Now.AddMinutes(-1));
        store.Connect(Cluster, second, Now);
        Assert.Single(store.GetCluster(Cluster, Now)!.ObserverViews);
        Assert.Equal(second, store.GetCluster(Cluster, Now)!.ObserverViews[0].Identity);

        store.Append(Cluster, second,
        [
            Event(1, Now.AddSeconds(1)), Event(1, Now.AddSeconds(1)), Event(2, Now.AddSeconds(2)), Event(3, Now.AddSeconds(3))
        ], Now);
        var events = store.GetEvents(Cluster, take: 99);
        Assert.Equal([3L, 2L], events.Select(item => item.Sequence));
        Assert.Single(store.GetEvents(Cluster, afterSequence: 2));
        Assert.Empty(store.GetEvents(new("x", "x", "x", "")));
    }

    [Fact]
    public void Cluster_list_is_sorted()
    {
        var store = CreateStore();
        var observer = Identity("a");
        store.Connect(new("Z", "b", "x", ""), observer, Now);
        store.Connect(new("A", "z", "x", ""), observer, Now);
        Assert.Equal(["A", "Z"], store.GetClusters(Now).Select(item => item.Key.Environment));
    }

    private static InMemoryCrowCastControlPlaneStore CreateStore(int maxEvents = 10, int maxObservers = 10) => new(Options.Create(new CrowCastControlPlaneOptions
    {
        ObserverStaleAfter = TimeSpan.FromSeconds(20),
        ObserverOfflineAfter = TimeSpan.FromMinutes(2),
        MaximumEventsPerCluster = maxEvents,
        MaximumObserversPerCluster = maxObservers
    }));

    private static CrowCastObservationSnapshot Snapshot(
        CrowCastObserverIdentity observer,
        CrowCastObserverIdentity member,
        string status,
        CrowCastObserverIdentity coordinator) => new(
            1,
            Cluster,
            observer,
            1,
            Now,
            coordinator,
            [new(member, status, Now, Now, null, new Dictionary<string, string>(), member == coordinator, member == observer)],
            new(0, 0, 0, 0, 0, 0, 0, 0, 0));

    private static CrowCastObservationEvent Event(long sequence, DateTimeOffset time) =>
        new(sequence, time, "membership", $"event-{sequence}");

    private static CrowCastMemberObservation Member(string identity, string status) =>
        new(Identity(identity), status, Now, Now, null, new Dictionary<string, string>(), false, false);

    private static CrowCastObserverIdentity Identity(string value) => new(value, $"{value}-generation");
}
