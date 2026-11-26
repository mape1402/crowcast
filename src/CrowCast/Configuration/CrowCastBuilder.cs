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
        var handlerType = typeof(THandler);
        var handlerContracts = handlerType.GetInterfaces()
            .Where(type =>
                type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(ICrowCastEventHandler<>))
            .Distinct()
            .ToArray();
        if (handlerContracts.Length == 0)
        {
            throw new InvalidOperationException(
                $"{handlerType} must implement at least one {typeof(ICrowCastEventHandler<>)} contract.");
        }

        var markerType = typeof(CrowCastEventHandlerRegistration<>).MakeGenericType(handlerType);
        if (Services.Any(descriptor => descriptor.ServiceType == markerType))
        {
            return this;
        }

        Services.AddSingleton(markerType, Activator.CreateInstance(markerType, nonPublic: true)!);
        Services.TryAddSingleton<THandler>();
        foreach (var handlerContract in handlerContracts)
        {
            Services.AddSingleton(handlerContract, provider => provider.GetRequiredService<THandler>());
        }

        return this;
    }

    private sealed class CrowCastEventHandlerRegistration<THandler>
        where THandler : class, ICrowCastEventHandler
    {
    }
}
