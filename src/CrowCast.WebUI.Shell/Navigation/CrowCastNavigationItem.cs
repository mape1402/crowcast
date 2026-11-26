namespace CrowCast.WebUI.Shell.Navigation;

public sealed record CrowCastNavigationItem(string Label, string Area, string Page, string Section, int Order, string Icon = "•");
