using CrowCast.Events;

namespace CrowCast.Tests;

internal sealed class RecordingEventHandler :
    ICrowCastEventHandler<CrowCastMemberChanged>,
    ICrowCastEventHandler<CrowCastMessageReceived>,
    ICrowCastEventHandler<CrowCastCoordinatorChanged>
{
    private readonly List<CrowCastEvent> _events = [];
    private readonly object _gate = new();

    public IReadOnlyCollection<CrowCastEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    public ValueTask HandleAsync(CrowCastMemberChanged @event, CancellationToken cancellationToken) =>
        RecordAsync(@event);

    public ValueTask HandleAsync(CrowCastMessageReceived @event, CancellationToken cancellationToken) =>
        RecordAsync(@event);

    public ValueTask HandleAsync(CrowCastCoordinatorChanged @event, CancellationToken cancellationToken) =>
        RecordAsync(@event);

    private ValueTask RecordAsync(CrowCastEvent @event)
    {
        lock (_gate)
        {
            _events.Add(@event);
        }

        return ValueTask.CompletedTask;
    }
}
