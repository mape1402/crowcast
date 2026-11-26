using CrowCast.ControlPlane.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages;

public sealed class IndexModel(
    ICrowCastControlPlaneStore store,
    TimeProvider timeProvider,
    IOptions<CrowCastControlPlaneWebUIOptions> options)
    : ControlPlanePageModel(store, timeProvider)
{
    private readonly string _dashboardPath = $"/{ServiceCollectionExtensions.Normalize(options.Value.RoutePrefix)}/clusters/dashboard";
    public int ClusterCount { get; private set; }
    public int ObserverCount { get; private set; }
    public int FreshObserverCount { get; private set; }
    public int HealthyCount { get; private set; }
    public int AttentionCount { get; private set; }
    public int AliveMemberCount { get; private set; }
    public int SuspectMemberCount { get; private set; }
    public int UnavailableMemberCount { get; private set; }

    public void OnGet()
    {
        var clusters = Store.GetClusters(TimeProvider.GetUtcNow());
        ClusterCount = clusters.Count;
        ObserverCount = clusters.Sum(cluster => cluster.Observers);
        FreshObserverCount = clusters.Sum(cluster => cluster.FreshObservers);
        HealthyCount = clusters.Count(cluster => cluster.Health == "Healthy");
        AttentionCount = ClusterCount - HealthyCount;
        AliveMemberCount = clusters.Sum(cluster => cluster.AliveMembers);
        SuspectMemberCount = clusters.Sum(cluster => cluster.SuspectMembers);
        UnavailableMemberCount = clusters.Sum(cluster => cluster.DeadMembers + cluster.LeftMembers);
    }

    public IActionResult OnGetClusters() => new JsonResult(new { items = ClusterRows.Create(Store, TimeProvider.GetUtcNow(), _dashboardPath) });
}
