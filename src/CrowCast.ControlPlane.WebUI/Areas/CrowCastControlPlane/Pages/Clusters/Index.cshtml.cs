using CrowCast.ControlPlane.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages.Clusters;

public sealed class IndexModel(
    ICrowCastControlPlaneStore store,
    TimeProvider timeProvider,
    IOptions<CrowCastControlPlaneWebUIOptions> options)
    : ControlPlanePageModel(store, timeProvider)
{
    private readonly string _dashboardPath = $"/{ServiceCollectionExtensions.Normalize(options.Value.RoutePrefix)}/clusters/dashboard";

    public IActionResult OnGetList() => new JsonResult(ClusterRows.Create(Store, TimeProvider.GetUtcNow(), _dashboardPath));
}
