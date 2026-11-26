using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Zoorce.Web.UI.Abstractions.Navigation;

namespace CrowCast.WebUI.Shell.Navigation;

internal sealed class ZoorceCrowCastNavigationProvider(
    CrowCastNavigationRegistry registry,
    LinkGenerator links,
    IHttpContextAccessor contextAccessor) : INavigationProvider
{
    public IReadOnlyList<NavigationItem> GetItems()
    {
        var context = contextAccessor.HttpContext;
        if (context is null) return [];
        return registry.GetItems().GroupBy(item => item.Section, StringComparer.OrdinalIgnoreCase)
            .Select(group => new NavigationItem(
                group.Key,
                Icon: group.Key.Equals("Control plane", StringComparison.OrdinalIgnoreCase) ? "◎" : "◉",
                Children: group.Select(item => new NavigationItem(
                    item.Label,
                    links.GetPathByPage(context, item.Page, values: new { area = item.Area }),
                    item.Icon,
                    MatchMode: NavigationMatchMode.Exact)).ToArray()))
            .ToArray();
    }
}
