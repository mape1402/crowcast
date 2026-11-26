using CrowCast.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrowCast.WebUI.Areas.CrowCast.Pages;

public sealed class ActivityModel(ICrowCastObservationSource observations, IOptions<CrowCastWebUIOptions> options)
    : CrowCastPageModel(observations, options)
{
    public IActionResult OnGetList() => new JsonResult(Observations.GetEvents(take: 1_000).Select(item => new
    {
        id = item.Sequence,
        item.OccurredAtUtc,
        item.Category,
        item.Summary,
        item.PreviousStatus,
        item.CurrentStatus,
        item.MessageType
    }));
}
