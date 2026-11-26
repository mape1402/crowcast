namespace CrowCast.Observability.Models;

public sealed record CrowCastObservationSample(
    DateTimeOffset ObservedAtUtc,
    int Alive,
    int Suspect,
    int Dead,
    int Left,
    CrowCastTelemetryCounters Counters);
