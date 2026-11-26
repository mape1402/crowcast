using CrowCast.Events;
using CrowCast.Messaging;
using Microsoft.Extensions.Logging;

namespace CrowCast.Internal;

internal sealed class CrowCastMessageRegistry
{
    private readonly IReadOnlyDictionary<Type, ICrowCastMessageContract> _byPayloadType;
    private readonly IReadOnlyDictionary<string, ICrowCastMessageContract> _byMessageType;

    public CrowCastMessageRegistry(IEnumerable<ICrowCastMessageContract> contracts)
    {
        var registered = contracts.ToArray();
        _byPayloadType = registered.ToDictionary(contract => contract.PayloadType);
        _byMessageType = registered.ToDictionary(contract => contract.MessageType, StringComparer.Ordinal);
    }

    public CrowCastMessage CreateMessage<TMessage>(
        TMessage message,
        IReadOnlyDictionary<string, string>? metadata)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!_byPayloadType.TryGetValue(typeof(TMessage), out var contract))
        {
            throw new InvalidOperationException(
                $"CrowCast message payload {typeof(TMessage)} is not registered. " +
                $"Register it with {nameof(Configuration.CrowCastBuilder)}.{nameof(Configuration.CrowCastBuilder.AddMessage)}.");
        }

        return contract.CreateMessage(message, metadata);
    }

    public async ValueTask DispatchAsync(
        CrowCastMessageReceived received,
        ICrowCast crowCast,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!_byMessageType.TryGetValue(received.Message.Type, out var contract))
        {
            return;
        }

        try
        {
            await contract.DispatchAsync(received, crowCast, logger, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "CrowCast could not deserialize message {MessageId} with registered type {MessageType}",
                received.Message.Id,
                received.Message.Type);
        }
    }
}
