using CrowCast.Observability;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CrowCast.WebUI.Areas.CrowCast.Pages;

public abstract class CrowCastPageModel(
    ICrowCastObservationSource observations,
    IOptions<CrowCastWebUIOptions> options) : PageModel
{
    protected ICrowCastObservationSource Observations { get; } = observations;

    public string LiveHubPath => $"/{ServiceCollectionExtensions.Normalize(options.Value.RoutePrefix)}/live";
}
