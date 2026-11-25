using CrowCast.Configuration;
using CrowCast.Diagnostics;
using CrowCast.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrowCast.DependencyInjection;

public static class CrowCastServiceCollectionExtensions
{
    public static CrowCastBuilder AddCrowCast(
        this IServiceCollection services,
        Action<CrowCastOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<CrowCastOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<CrowCastOptions>, CrowCastOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<CrowCastService>();
        services.TryAddSingleton<ICrowCast>(provider => provider.GetRequiredService<CrowCastService>());
        services.TryAddSingleton<ICrowCastDiagnostics>(provider => provider.GetRequiredService<CrowCastService>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<CrowCastService>());

        return new CrowCastBuilder(services);
    }
}
