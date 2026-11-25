using CrowCast.Events;

namespace CrowCast.Tests;

internal sealed class RecordingEventHandler : ICrowCastEventHandler
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

    public ValueTask HandleAsync(CrowCastEvent @event, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _events.Add(@event);
        }

        return ValueTask.CompletedTask;
    }
}
