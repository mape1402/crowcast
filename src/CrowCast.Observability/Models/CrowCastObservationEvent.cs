namespace CrowCast.Observability.Models;

public sealed record CrowCastObservationEvent(
    long Sequence,
    DateTimeOffset OccurredAtUtc,
    string Category,
    string Summary,
    CrowCastObserverIdentity? Subject = null,
    string? PreviousStatus = null,
    string? CurrentStatus = null,
    string? MessageType = null);
