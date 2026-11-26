using CrowCast.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrowCast.WebUI.Areas.CrowCast.Pages;

public sealed class MembersModel(ICrowCastObservationSource observations, IOptions<CrowCastWebUIOptions> options)
    : CrowCastPageModel(observations, options)
{
    public IActionResult OnGetList() => new JsonResult(Observations.GetSnapshot().Members.Select(member => new
    {
        id = member.Identity.ToString(),
        member.Identity.NodeId,
        member.Identity.Generation,
        member.Status,
        coordinator = member.IsCoordinator ? "Yes" : string.Empty,
        local = member.IsLocal ? "Yes" : string.Empty,
        member.LastChangedAtUtc,
        member.Endpoint
    }));
}
