using CrowCast.ControlPlane.Configuration;
using CrowCast.ControlPlane.Realtime;
using CrowCast.ControlPlane.Storage;
using CrowCast.Observability.Models;
using CrowCast.WebUI.Realtime;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests.WebUI;

public sealed class RealtimeTests
{
    [Fact]
    public async Task Local_hub_and_dispatcher_subscribe_and_publish_changes()
    {
        var hub = new CrowCastWebUIHub { Context = new StubHubCallerContext(), Groups = new RecordingGroupManager() };
        await hub.Watch();
        Assert.Contains(("connection", "crowcast:local"), ((RecordingGroupManager)hub.Groups).Added);

        var source = Source();
        var context = new RecordingHubContext<CrowCastWebUIHub>();
        var dispatcher = new CrowCastWebUIChangeDispatcher(source, context);
        await dispatcher.StartAsync(CancellationToken.None);
        source.Raise();
        await WaitForAsync(() => context.ClientsImpl.Proxy.Calls.Count > 0);
        Assert.Equal("crowcast.changed", context.ClientsImpl.Proxy.Calls[0].Method);
        await dispatcher.StopAsync(CancellationToken.None);
        dispatcher.Dispose();
    }

    [Fact]
    public async Task Control_plane_hub_and_dispatcher_publish_fleet_and_cluster_changes()
    {
        var groups = new RecordingGroupManager();
        var hub = new CrowCastControlPlaneHub { Context = new StubHubCallerContext(), Groups = groups };
        await hub.WatchFleet();
        await hub.WatchCluster("cluster");
        await hub.WatchCluster(" ");
        Assert.Equal(2, groups.Added.Count);

        var store = new InMemoryCrowCastControlPlaneStore(Options.Create(new CrowCastControlPlaneOptions()));
        var context = new RecordingHubContext<CrowCastControlPlaneHub>();
        var dispatcher = new CrowCastControlPlaneChangeDispatcher(store, context);
        await dispatcher.StartAsync(CancellationToken.None);
        var snapshot = Source().Snapshot;
        store.Upsert(snapshot, snapshot.ObservedAtUtc);
        await WaitForAsync(() => context.ClientsImpl.Proxy.Calls.Count > 0);
        Assert.Equal("crowcast.changed", context.ClientsImpl.Proxy.Calls[0].Method);
        await dispatcher.StopAsync(CancellationToken.None);
        dispatcher.Dispose();
    }

    private static StubObservationSource Source()
    {
        var observer = new CrowCastObserverIdentity("node", "generation");
        return new(new(1, new("Test", "service", "cluster", ""), observer, 0, DateTimeOffset.UtcNow, observer, [], new(0, 0, 0, 0, 0, 0, 0, 0, 0)));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var index = 0; index < 100 && !condition(); index++) await Task.Delay(10);
        Assert.True(condition());
    }
}
