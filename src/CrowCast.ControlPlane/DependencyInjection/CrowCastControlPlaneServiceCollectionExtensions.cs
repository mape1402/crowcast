using CrowCast.ControlPlane.Configuration;
using CrowCast.ControlPlane.Realtime;
using CrowCast.ControlPlane.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrowCast.ControlPlane.DependencyInjection;

public static class CrowCastControlPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddCrowCastControlPlane(
        this IServiceCollection services,
        Action<CrowCastControlPlaneOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGrpc();
        services.AddSignalR();
        var options = services.AddOptions<CrowCastControlPlaneOptions>();
        if (configure is not null) options.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CrowCastControlPlaneOptions>, CrowCastControlPlaneOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICrowCastControlPlaneStore, InMemoryCrowCastControlPlaneStore>();
        services.AddHostedService<CrowCastControlPlaneChangeDispatcher>();
        return services;
    }
}
