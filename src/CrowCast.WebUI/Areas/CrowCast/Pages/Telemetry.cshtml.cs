using CrowCast.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrowCast.WebUI.Areas.CrowCast.Pages;

public sealed class TelemetryModel(ICrowCastObservationSource observations, IOptions<CrowCastWebUIOptions> options)
    : CrowCastPageModel(observations, options)
{
    public IActionResult OnGetData() => new JsonResult(new { samples = Observations.GetSamples(500) });
}
