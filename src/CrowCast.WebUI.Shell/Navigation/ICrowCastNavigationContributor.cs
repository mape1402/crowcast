namespace CrowCast.WebUI.Shell.Navigation;

public interface ICrowCastNavigationContributor
{
    IReadOnlyCollection<CrowCastNavigationItem> GetItems();
}
