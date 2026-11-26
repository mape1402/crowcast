using CrowCast.ControlPlane.Client.Configuration;
using CrowCast.ControlPlane.Client.Internal;
using CrowCast.Observability.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrowCast.ControlPlane.Client.DependencyInjection;

public static class CrowCastControlPlaneClientServiceCollectionExtensions
{
    public static IServiceCollection AddCrowCastControlPlaneClient(
        this IServiceCollection services,
        Action<CrowCastControlPlaneClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddCrowCastObservability();
        services.AddOptions<CrowCastControlPlaneClientOptions>().Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CrowCastControlPlaneClientOptions>, CrowCastControlPlaneClientOptionsValidator>());
        services.TryAddSingleton<CrowCastControlPlaneReporter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CrowCastControlPlaneReporterHostedService>());
        return services;
    }
}
