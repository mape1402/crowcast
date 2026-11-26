using CrowCast.Events;
using CrowCast.Observability.Configuration;
using CrowCast.Observability.Models;
using Microsoft.Extensions.Options;

namespace CrowCast.Observability.Internal;

internal sealed class CrowCastObservationEventSink
{
    private readonly BoundedObservationBuffer<CrowCastObservationEvent> _events;
    private long _sequence;

    public CrowCastObservationEventSink(IOptions<CrowCastObservabilityOptions> options) =>
        _events = new(options.Value.MaximumEvents);

    public long Sequence => Interlocked.Read(ref _sequence);

    public event Action? Changed;

    public ValueTask HandleAsync(CrowCastEvent @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observation = @event switch
        {
            CrowCastMemberChanged member => CreateEvent(
                member.OccurredAtUtc,
                "membership",
                $"{member.Current.Identity} is {member.Current.Status}",
                CrowCastObserverIdentity.From(member.Current.Identity),
                member.Previous?.Status.ToString(),
                member.Current.Status.ToString()),
            CrowCastCoordinatorChanged coordinator => CreateEvent(
                coordinator.OccurredAtUtc,
                "coordination",
                coordinator.Current is null ? "Coordinator cleared" : $"Coordinator changed to {coordinator.Current}",
                coordinator.Current is null ? null : CrowCastObserverIdentity.From(coordinator.Current)),
            CrowCastMessageReceived message => CreateEvent(
                message.OccurredAtUtc,
                "message",
                $"Received {message.Message.Type}",
                message.Message.Origin is null ? null : CrowCastObserverIdentity.From(message.Message.Origin),
                messageType: message.Message.Type),
            _ => null
        };

        if (observation is not null)
        {
            _events.Add(observation);
            Changed?.Invoke();
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<CrowCastObservationEvent> GetEvents(long afterSequence, int take) =>
        _events.Snapshot().Where(item => item.Sequence > afterSequence).OrderByDescending(item => item.Sequence)
            .Take(Math.Clamp(take, 1, 1_000)).ToArray();

    private CrowCastObservationEvent CreateEvent(
        DateTimeOffset occurredAtUtc,
        string category,
        string summary,
        CrowCastObserverIdentity? subject = null,
        string? previousStatus = null,
        string? currentStatus = null,
        string? messageType = null) =>
        new(Interlocked.Increment(ref _sequence), occurredAtUtc, category, summary, subject, previousStatus, currentStatus, messageType);
}
