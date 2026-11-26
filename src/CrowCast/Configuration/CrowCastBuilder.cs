using CrowCast.Events;
using CrowCast.Internal;
using CrowCast.Messaging;
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

    public CrowCastBuilder AddMessage<TMessage>(string messageType)
        where TMessage : notnull
    {
        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("A CrowCast message type is required.", nameof(messageType));
        }

        var normalizedType = messageType.Trim();
        var registrations = Services
            .Where(descriptor => descriptor.ServiceType == typeof(CrowCastMessageContractRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CrowCastMessageContractRegistration>()
            .ToArray();
        var payloadRegistration = registrations.SingleOrDefault(
            registration => registration.PayloadType == typeof(TMessage));
        if (payloadRegistration is not null)
        {
            if (!string.Equals(payloadRegistration.MessageType, normalizedType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"CrowCast payload {typeof(TMessage)} is already registered as {payloadRegistration.MessageType}.");
            }

            return this;
        }

        var typeRegistration = registrations.SingleOrDefault(
            registration => string.Equals(registration.MessageType, normalizedType, StringComparison.Ordinal));
        if (typeRegistration is not null)
        {
            throw new InvalidOperationException(
                $"CrowCast message type {normalizedType} is already registered for {typeRegistration.PayloadType}.");
        }

        Services.AddSingleton(new CrowCastMessageContractRegistration(typeof(TMessage), normalizedType));
        Services.AddSingleton<ICrowCastMessageContract>(provider =>
            new CrowCastMessageContract<TMessage>(
                normalizedType,
                provider.GetRequiredService<ICrowCastMessageSerializer>(),
                provider.GetRequiredService<IServiceScopeFactory>()));
        return this;
    }

    public CrowCastBuilder AddMessageHandler<TMessage, THandler>(string messageType)
        where TMessage : notnull
        where THandler : class, ICrowCastMessageHandler<TMessage>
    {
        AddMessage<TMessage>(messageType);

        var markerType = typeof(CrowCastMessageHandlerRegistration<,>).MakeGenericType(
            typeof(TMessage),
            typeof(THandler));
        if (Services.Any(descriptor => descriptor.ServiceType == markerType))
        {
            return this;
        }

        Services.AddSingleton(markerType, Activator.CreateInstance(markerType, nonPublic: true)!);
        Services.TryAddScoped<THandler>();
        Services.AddScoped<ICrowCastMessageHandler<TMessage>>(
            provider => provider.GetRequiredService<THandler>());
        return this;
    }

    public CrowCastBuilder UseMessageSerializer<TSerializer>()
        where TSerializer : class, ICrowCastMessageSerializer
    {
        Services.RemoveAll<ICrowCastMessageSerializer>();
        Services.AddSingleton<ICrowCastMessageSerializer, TSerializer>();
        return this;
    }

    private sealed class CrowCastEventHandlerRegistration<THandler>
        where THandler : class, ICrowCastEventHandler
    {
    }

    private sealed class CrowCastMessageHandlerRegistration<TMessage, THandler>
        where TMessage : notnull
        where THandler : class, ICrowCastMessageHandler<TMessage>
    {
    }
}
