using CrowCast.WebUI.Shell.Navigation;

namespace CrowCast.ControlPlane.WebUI.Navigation;

internal sealed class ControlPlaneNavigationContributor : ICrowCastNavigationContributor
{
    public IReadOnlyCollection<CrowCastNavigationItem> GetItems() =>
    [
        new("Fleet overview", "CrowCastControlPlane", "/Index", "Control plane", 100, "◉"),
        new("Clusters", "CrowCastControlPlane", "/Clusters/Index", "Control plane", 101, "◎")
    ];
}
