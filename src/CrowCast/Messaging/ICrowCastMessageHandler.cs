namespace CrowCast.Messaging;

public interface ICrowCastMessageHandler<TMessage>
    where TMessage : notnull
{
    ValueTask HandleAsync(
        CrowCastMessageContext<TMessage> context,
        CancellationToken cancellationToken);
}
