using CrowCast.Events;

namespace CrowCast.Observability.Internal;

internal sealed class CrowCastObservationEventHandler(CrowCastObservationEventSink sink) :
    ICrowCastEventHandler<CrowCastMemberChanged>,
    ICrowCastEventHandler<CrowCastMessageReceived>,
    ICrowCastEventHandler<CrowCastCoordinatorChanged>
{
    public ValueTask HandleAsync(CrowCastMemberChanged @event, CancellationToken cancellationToken) =>
        sink.HandleAsync(@event, cancellationToken);

    public ValueTask HandleAsync(CrowCastMessageReceived @event, CancellationToken cancellationToken) =>
        sink.HandleAsync(@event, cancellationToken);

    public ValueTask HandleAsync(CrowCastCoordinatorChanged @event, CancellationToken cancellationToken) =>
        sink.HandleAsync(@event, cancellationToken);
}
