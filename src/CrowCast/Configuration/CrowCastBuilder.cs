using CrowCast.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrowCast.Configuration;

public sealed class CrowCastBuilder
{
    internal CrowCastBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public CrowCastBuilder AddEventHandler<THandler>()
        where THandler : class, ICrowCastEventHandler
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICrowCastEventHandler, THandler>());
        return this;
    }
}
