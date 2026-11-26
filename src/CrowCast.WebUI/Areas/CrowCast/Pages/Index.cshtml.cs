using CrowCast.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrowCast.WebUI.Areas.CrowCast.Pages;

public sealed class IndexModel(
    ICrowCastObservationSource observations,
    IOptions<CrowCastWebUIOptions> options) : CrowCastPageModel(observations, options)
{
    public Observability.Models.CrowCastObservationSnapshot Snapshot { get; private set; } = null!;

    public void OnGet() => Snapshot = Observations.GetSnapshot();

    public IActionResult OnGetData() => new JsonResult(new
    {
        snapshot = Observations.GetSnapshot(),
        members = Observations.GetSnapshot().Members,
        samples = Observations.GetSamples(300)
    });
}
