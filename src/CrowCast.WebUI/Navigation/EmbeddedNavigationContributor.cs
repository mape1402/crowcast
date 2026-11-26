using CrowCast.WebUI.Shell.Navigation;

namespace CrowCast.WebUI.Navigation;

internal sealed class EmbeddedNavigationContributor : ICrowCastNavigationContributor
{
    public IReadOnlyCollection<CrowCastNavigationItem> GetItems() =>
    [
        new("Overview", "CrowCast", "/Index", "Local cluster", 10, "◉"),
        new("Members", "CrowCast", "/Members", "Local cluster", 11, "◎"),
        new("Activity", "CrowCast", "/Activity", "Local cluster", 12, "↗"),
        new("Telemetry", "CrowCast", "/Telemetry", "Local cluster", 13, "⌁"),
        new("Topology", "CrowCast", "/Topology", "Local cluster", 14, "◇")
    ];
}
