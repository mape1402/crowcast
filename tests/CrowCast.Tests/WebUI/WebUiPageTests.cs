using System.Text.Json;
using CrowCast.ControlPlane.Configuration;
using CrowCast.ControlPlane.Storage;
using CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages;
using CrowCast.Observability.Models;
using CrowCast.WebUI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests.WebUI;

public sealed class WebUiPageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Embedded_page_models_return_snapshots_rows_events_and_samples()
    {
        var source = CreateSource();
        source.Events.Add(new(1, Now, "membership", "joined", CurrentStatus: "Alive"));
        source.Samples.Add(new(Now, 1, 0, 0, 0, source.Snapshot.Counters));
        var options = Options.Create(new CrowCastWebUIOptions { RoutePrefix = "/ops/crowcast/" });

        var index = new CrowCast.WebUI.Areas.CrowCast.Pages.IndexModel(source, options);
        index.OnGet();
        Assert.Equal(source.Snapshot, index.Snapshot);
        Assert.Contains("members", Json(index.OnGetData()));
        Assert.Equal("/ops/crowcast/live", index.LiveHubPath);

        var members = new CrowCast.WebUI.Areas.CrowCast.Pages.MembersModel(source, options);
        Assert.Contains("node", Json(members.OnGetList()));
        var activity = new CrowCast.WebUI.Areas.CrowCast.Pages.ActivityModel(source, options);
        Assert.Contains("joined", Json(activity.OnGetList()));
        var telemetry = new CrowCast.WebUI.Areas.CrowCast.Pages.TelemetryModel(source, options);
        Assert.Contains("alive", Json(telemetry.OnGetData()), StringComparison.OrdinalIgnoreCase);
        var topology = new CrowCast.WebUI.Areas.CrowCast.Pages.TopologyModel(source, options);
        Assert.Contains("members", Json(topology.OnGetData()));
    }

    [Fact]
    public void Control_plane_page_models_return_fleet_and_cluster_data()
    {
        var store = new InMemoryCrowCastControlPlaneStore(Options.Create(new CrowCastControlPlaneOptions()));
        store.Upsert(CreateSource().Snapshot, Now);

        var controlOptions = Options.Create(new CrowCast.ControlPlane.WebUI.CrowCastControlPlaneWebUIOptions { RoutePrefix = "ops/fleet" });
        var fleet = new CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages.IndexModel(store, new FixedTimeProvider(Now), controlOptions);
        fleet.OnGet();
        Assert.Equal(1, fleet.ClusterCount);
        Assert.Equal(1, fleet.ObserverCount);
        Assert.Equal(1, fleet.FreshObserverCount);
        Assert.Equal(1, fleet.HealthyCount);
        Assert.Equal(0, fleet.AttentionCount);
        Assert.Equal(1, fleet.AliveMemberCount);
        Assert.Equal(0, fleet.SuspectMemberCount);
        Assert.Equal(0, fleet.UnavailableMemberCount);
        Assert.Contains("orders", Json(fleet.OnGetClusters()));
        Assert.Contains("/ops/fleet/clusters/dashboard", Json(fleet.OnGetClusters()));
        Assert.Equal("/_crowcast/control-plane/live", fleet.LiveHubPath);

        var clusters = new CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages.Clusters.IndexModel(store, new FixedTimeProvider(Now), controlOptions);
        var clusterRows = Json(clusters.OnGetList());
        Assert.Contains("orders", clusterRows);
        Assert.Contains("View dashboard", clusterRows);
        Assert.Contains("freshObservers", clusterRows);
        Assert.Contains("deadMembers", clusterRows);
        Assert.Contains("leftMembers", clusterRows);
        Assert.Contains("coordinatorCount", clusterRows);
        Assert.Contains("In agreement", clusterRows);

        var detail = new CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages.Clusters.DetailModel(store, new FixedTimeProvider(Now));
        Assert.IsType<PageResult>(detail.OnGet("Production", "orders", "alpha", "west"));
        Assert.Equal("Healthy", detail.ClusterView.Health);
        Assert.Equal("node", detail.Coordinator!.NodeId);
        Assert.Equal(new CrowCastTelemetryCounters(1, 2, 3, 4, 5, 6, 7, 8, 9), detail.Counters);
        Assert.Contains("node", Json(detail.OnGetObservers("Production", "orders", "alpha", "west")));
        var detailData = Json(detail.OnGetData("Production", "orders", "alpha", "west"));
        Assert.Contains("members", detailData);
        Assert.Contains("counters", detailData);
        Assert.Contains("coordinator", detailData);
        Assert.Contains("observedAtUtc", detailData);
        var detailMembers = Json(detail.OnGetMembers("Production", "orders", "alpha", "west"));
        Assert.Contains("seenBy", detailMembers);
        Assert.Contains("observers", detailMembers);
        Assert.Contains("In agreement", detailMembers);
        Assert.Equal("[]", Json(detail.OnGetEvents("Production", "orders", "alpha", "west")));

        var missing = new CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages.Clusters.DetailModel(store, new FixedTimeProvider(Now));
        Assert.IsType<NotFoundResult>(missing.OnGet("missing", "missing", "missing"));
        Assert.Equal("[]", Json(missing.OnGetObservers("missing", "missing", "missing")));
        Assert.Contains("\"members\":[]", Json(missing.OnGetData("missing", "missing", "missing")));
        Assert.Equal("[]", Json(missing.OnGetMembers("missing", "missing", "missing")));
        Assert.IsType<NotFoundResult>(missing.OnGet(null!, null!, null!, null!));

        var emptyCluster = new CrowCastClusterKey("Empty", "orders", "alpha", "");
        store.Connect(emptyCluster, new CrowCastObserverIdentity("empty", "generation"), Now);
        Assert.Contains("\"members\":[]", Json(missing.OnGetData("Empty", "orders", "alpha")));
        Assert.Equal("[]", Json(missing.OnGetMembers("Empty", "orders", "alpha")));
    }

    [Fact]
    public void Control_plane_cluster_projection_is_stable_and_aggregates_fresh_observers()
    {
        var store = new InMemoryCrowCastControlPlaneStore(Options.Create(new CrowCastControlPlaneOptions()));
        var cluster = new CrowCastClusterKey("Production", "orders", "alpha", "west");
        var first = new CrowCastObserverIdentity("a", "a-generation");
        var second = new CrowCastObserverIdentity("b", "b-generation");
        var shared = new CrowCastObserverIdentity("shared", "shared-generation");
        var onlyFirst = new CrowCastObserverIdentity("only-first", "only-first-generation");
        store.Upsert(new CrowCastObservationSnapshot(1, cluster, first, 1, Now, first,
        [
            new(first, "Alive", Now, Now, null, new Dictionary<string, string>(), true, true),
            new(shared, "Alive", Now, Now, "tcp://shared", new Dictionary<string, string>(), false, false),
            new(onlyFirst, "Alive", Now, Now, null, new Dictionary<string, string>(), false, false)
        ], new(1, 1, 1, 1, 1, 1, 1, 1, 1)), Now);
        store.Upsert(new CrowCastObservationSnapshot(1, cluster, second, 1, Now, first,
        [
            new(first, "Alive", Now, Now.AddSeconds(1), "tcp://a", new Dictionary<string, string>(), true, false),
            new(shared, "Suspect", Now, Now.AddSeconds(1), null, new Dictionary<string, string>(), false, false)
        ], new(2, 2, 2, 2, 2, 2, 2, 2, 2)), Now);
        var detail = new CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages.Clusters.DetailModel(store, new FixedTimeProvider(Now));

        Assert.IsType<PageResult>(detail.OnGet("Production", "orders", "alpha", "west"));
        Assert.Equal(first, detail.Coordinator);
        Assert.Equal(new CrowCastTelemetryCounters(3, 3, 3, 3, 3, 3, 3, 3, 3), detail.Counters);
        var members = Json(detail.OnGetMembers("Production", "orders", "alpha", "west"));
        Assert.Contains("\"seenBy\":2", members);
        Assert.Contains("\"seenBy\":1", members);
        Assert.Contains("In agreement", members);
        Assert.Contains("Disagreement", members);
        Assert.Contains("tcp://shared", members);
        var data = Json(detail.OnGetData("Production", "orders", "alpha", "west"));
        Assert.Contains("\"messagesPublished\":3", data);
        Assert.Contains("\"nodeId\":\"a\"", data);

        var stale = new CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages.Clusters.DetailModel(store, new FixedTimeProvider(Now.AddMinutes(3)));
        Assert.Contains("\"messagesPublished\":3", Json(stale.OnGetData("Production", "orders", "alpha", "west")));
    }

    private static StubObservationSource CreateSource()
    {
        var observer = new CrowCastObserverIdentity("node", "generation");
        var counters = new CrowCastTelemetryCounters(1, 2, 3, 4, 5, 6, 7, 8, 9);
        var snapshot = new CrowCastObservationSnapshot(
            1,
            new("Production", "orders", "alpha", "west"),
            observer,
            1,
            Now,
            observer,
            [new(observer, "Alive", Now, Now, null, new Dictionary<string, string>(), true, true)],
            counters);
        return new(snapshot);
    }

    private static string Json(IActionResult result) => JsonSerializer.Serialize(Assert.IsType<JsonResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
