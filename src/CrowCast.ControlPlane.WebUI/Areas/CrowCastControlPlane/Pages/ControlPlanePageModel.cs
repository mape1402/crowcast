using CrowCast.ControlPlane.Storage;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages;

public abstract class ControlPlanePageModel(
    ICrowCastControlPlaneStore store,
    TimeProvider timeProvider) : PageModel
{
    protected ICrowCastControlPlaneStore Store { get; } = store;

    protected TimeProvider TimeProvider { get; } = timeProvider;

    public string LiveHubPath => "/_crowcast/control-plane/live";
}
