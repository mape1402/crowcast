using CrowCast.Observability.DependencyInjection;
using CrowCast.WebUI.Navigation;
using CrowCast.WebUI.Realtime;
using CrowCast.WebUI.Shell;
using CrowCast.WebUI.Shell.Navigation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrowCast.WebUI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCrowCastWebUI(
        this IServiceCollection services,
        Action<CrowCastWebUIOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCrowCastObservability();
        services.AddCrowCastWebUIShell();
        services.AddSignalR();
        services.AddRazorPages();
        var options = services.AddOptions<CrowCastWebUIOptions>();
        if (configure is not null) options.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<RazorPagesOptions>, ConfigureCrowCastRoutes>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICrowCastNavigationContributor, EmbeddedNavigationContributor>());
        services.AddHostedService<CrowCastWebUIChangeDispatcher>();
        return services;
    }

    private sealed class ConfigureCrowCastRoutes(IOptions<CrowCastWebUIOptions> options) : IConfigureOptions<RazorPagesOptions>
    {
        public void Configure(RazorPagesOptions value)
        {
            if (options.Value.ExcludeFromOpenApi)
            {
                value.Conventions.AddAreaFolderApplicationModelConvention(
                    "CrowCast",
                    "/",
                    model => model.EndpointMetadata.Add(new ExcludeFromDescriptionAttribute()));
            }

            var prefix = Normalize(options.Value.RoutePrefix);
            if (prefix.Equals("CrowCast", StringComparison.OrdinalIgnoreCase)) return;
            value.Conventions.AddAreaPageRoute("CrowCast", "/Index", prefix);
            value.Conventions.AddAreaPageRoute("CrowCast", "/Members", $"{prefix}/members");
            value.Conventions.AddAreaPageRoute("CrowCast", "/Activity", $"{prefix}/activity");
            value.Conventions.AddAreaPageRoute("CrowCast", "/Telemetry", $"{prefix}/telemetry");
            value.Conventions.AddAreaPageRoute("CrowCast", "/Topology", $"{prefix}/topology");
        }
    }

    internal static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "crowcast" : value.Trim('/');
}
