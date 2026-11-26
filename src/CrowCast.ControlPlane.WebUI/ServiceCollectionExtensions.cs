using CrowCast.ControlPlane.DependencyInjection;
using CrowCast.ControlPlane.WebUI.Navigation;
using CrowCast.WebUI.Shell;
using CrowCast.WebUI.Shell.Navigation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrowCast.ControlPlane.WebUI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCrowCastControlPlaneWebUI(
        this IServiceCollection services,
        Action<CrowCastControlPlaneWebUIOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCrowCastControlPlane();
        services.AddCrowCastWebUIShell();
        services.AddRazorPages();
        var options = services.AddOptions<CrowCastControlPlaneWebUIOptions>();
        if (configure is not null) options.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<RazorPagesOptions>, ConfigureRoutes>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICrowCastNavigationContributor, ControlPlaneNavigationContributor>());
        return services;
    }

    private sealed class ConfigureRoutes(IOptions<CrowCastControlPlaneWebUIOptions> options) : IConfigureOptions<RazorPagesOptions>
    {
        public void Configure(RazorPagesOptions value)
        {
            var prefix = Normalize(options.Value.RoutePrefix);
            value.Conventions.AddAreaPageRoute("CrowCastControlPlane", "/Index", prefix);
            value.Conventions.AddAreaPageRoute("CrowCastControlPlane", "/Clusters/Index", $"{prefix}/clusters");
            value.Conventions.AddAreaPageRoute("CrowCastControlPlane", "/Clusters/Detail", $"{prefix}/clusters/detail");
            value.Conventions.AddAreaPageRoute("CrowCastControlPlane", "/Clusters/Detail", $"{prefix}/clusters/dashboard");
        }
    }

    internal static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "crowcast-control" : value.Trim('/');
}
