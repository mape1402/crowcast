using CrowCast.Events;

namespace CrowCast.Observability.Internal;

internal sealed class CrowCastObservationEventHandler(CrowCastObservationEventSink sink) : ICrowCastEventHandler
{
    public ValueTask HandleAsync(CrowCastEvent @event, CancellationToken cancellationToken) =>
        sink.HandleAsync(@event, cancellationToken);
}
