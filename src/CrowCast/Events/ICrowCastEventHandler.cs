namespace CrowCast.Events;

public interface ICrowCastEventHandler
{
    ValueTask HandleAsync(CrowCastEvent @event, CancellationToken cancellationToken);
}
