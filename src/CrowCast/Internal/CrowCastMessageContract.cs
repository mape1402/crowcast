using CrowCast.Events;
using CrowCast.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrowCast.Internal;

internal interface ICrowCastMessageContract
{
    string MessageType { get; }

    Type PayloadType { get; }

    CrowCastMessage CreateMessage(object payload, IReadOnlyDictionary<string, string>? metadata);

    ValueTask DispatchAsync(
        CrowCastMessageReceived received,
        ICrowCast crowCast,
        ILogger logger,
        CancellationToken cancellationToken);
}

internal sealed class CrowCastMessageContract<TMessage> : ICrowCastMessageContract
    where TMessage : notnull
{
    private readonly ICrowCastMessageSerializer _serializer;
    private readonly IServiceScopeFactory _scopeFactory;

    public CrowCastMessageContract(
        string messageType,
        ICrowCastMessageSerializer serializer,
        IServiceScopeFactory scopeFactory)
    {
        MessageType = messageType;
        _serializer = serializer;
        _scopeFactory = scopeFactory;
    }

    public string MessageType { get; }

    public Type PayloadType => typeof(TMessage);

    public CrowCastMessage CreateMessage(
        object payload,
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (payload is not TMessage message)
        {
            throw new ArgumentException(
                $"CrowCast message contract {MessageType} requires payload {typeof(TMessage)}.",
                nameof(payload));
        }

        return CrowCastMessage.Create(MessageType, _serializer.Serialize(message), metadata);
    }

    public async ValueTask DispatchAsync(
        CrowCastMessageReceived received,
        ICrowCast crowCast,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handlers = scope.ServiceProvider
            .GetServices<ICrowCastMessageHandler<TMessage>>()
            .ToArray();
        if (handlers.Length == 0)
        {
            return;
        }

        var message = _serializer.Deserialize<TMessage>(received.Message.Content);
        var context = new CrowCastMessageContext<TMessage>(
            message,
            received.Message,
            received.OccurredAtUtc,
            crowCast);

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "CrowCast message handler {HandlerType} failed while processing {MessageType}",
                    handler.GetType(),
                    MessageType);
            }
        }
    }
}

internal sealed record CrowCastMessageContractRegistration(Type PayloadType, string MessageType);
