using CrowCast.Backends;
using CrowCast.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrowCast.DependencyInjection;

public static class CrowCastNodeDiscoveryBuilderExtensions
{
    public static CrowCastBuilder UseStaticNodeDiscovery(
        this CrowCastBuilder builder,
        params CrowCastEndpoint[] endpoints)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoints);

        builder.Services.RemoveAll<ICrowCastNodeDiscovery>();
        builder.Services.AddSingleton<ICrowCastNodeDiscovery>(new StaticCrowCastNodeDiscovery(endpoints));
        return builder;
    }
}
