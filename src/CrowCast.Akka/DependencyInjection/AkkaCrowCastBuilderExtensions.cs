using CrowCast.Akka.Configuration;
using CrowCast.Akka.Internal;
using CrowCast.Backends;
using CrowCast.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrowCast.Akka.DependencyInjection;

public static class AkkaCrowCastBuilderExtensions
{
    public static CrowCastBuilder UseAkka(
        this CrowCastBuilder builder,
        Action<AkkaCrowCastOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Services.AddOptions<AkkaCrowCastOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AkkaCrowCastOptions>, AkkaCrowCastOptionsValidator>());
        builder.Services.TryAddSingleton<ICrowCastNodeDiscovery>(
            new StaticCrowCastNodeDiscovery(Array.Empty<CrowCastEndpoint>()));
        builder.Services.TryAddSingleton<AkkaCrowCastBackend>();
        builder.Services.RemoveAll<ICrowCastBackend>();
        builder.Services.AddSingleton<ICrowCastBackend>(
            provider => provider.GetRequiredService<AkkaCrowCastBackend>());

        return builder;
    }
}
