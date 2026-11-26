using CrowCast.WebUI.Shell.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zoorce.Web.UI;
using Zoorce.Web.UI.Abstractions.Navigation;
using Zoorce.Web.UI.Layout;
using Zoorce.Web.UI.Layout.Components;
using Zoorce.Web.UI.Theming;

namespace CrowCast.WebUI.Shell;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCrowCastWebUIShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<INavigationProvider, ZoorceCrowCastNavigationProvider>();
        services.TryAddSingleton<CrowCastNavigationRegistry>();
        services.AddHttpContextAccessor();
        services.AddRazorPages().AddZoorceWebUI(options =>
        {
            options.ApplicationName = "CrowCast";
            options.BrandMark = "C";
            options.DefaultTheme = ZoorceThemeMode.Light;
            options.UseThemePreset(ZoorceThemePreset.Teal);
            options.ThemeSelector = null;
            options.SidebarMode = ZoorceSidebarMode.Collapsible;
            options.NavigationDensity = ZoorceNavigationDensity.Compact;
            options.NavigationExpansion = ZoorceNavigationExpansionMode.ActivePath;
            options.PersistNavigationExpansion = true;
            options.PersistSidebarState = true;
        });
        return services;
    }
}
