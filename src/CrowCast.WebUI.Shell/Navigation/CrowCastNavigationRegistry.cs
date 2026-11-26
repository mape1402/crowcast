namespace CrowCast.WebUI.Shell.Navigation;

public sealed class CrowCastNavigationRegistry(IEnumerable<ICrowCastNavigationContributor> contributors)
{
    public IReadOnlyList<CrowCastNavigationItem> GetItems() => contributors.SelectMany(contributor => contributor.GetItems())
        .OrderBy(item => item.Order).ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToArray();
}
