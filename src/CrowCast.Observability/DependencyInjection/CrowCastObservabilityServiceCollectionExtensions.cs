using CrowCast.Events;
using CrowCast.Observability.Configuration;
using CrowCast.Observability.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrowCast.Observability.DependencyInjection;

public static class CrowCastObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddCrowCastObservability(
        this IServiceCollection services,
        Action<CrowCastObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<CrowCastObservabilityOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CrowCastObservabilityOptions>, CrowCastObservabilityOptionsValidator>());
        services.TryAddSingleton<CrowCastObservationEventSink>();
        services.TryAddSingleton<CrowCastObservationSource>();
        services.TryAddSingleton<ICrowCastObservationSource>(provider => provider.GetRequiredService<CrowCastObservationSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICrowCastEventHandler, CrowCastObservationEventHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CrowCastObservationHostedService>());
        return services;
    }
}
