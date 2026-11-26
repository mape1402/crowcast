namespace CrowCast.Events;

public interface ICrowCastEventHandler
{
}

public interface ICrowCastEventHandler<in TEvent> : ICrowCastEventHandler
    where TEvent : CrowCastEvent
{
    ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
