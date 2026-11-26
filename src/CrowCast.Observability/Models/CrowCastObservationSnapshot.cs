namespace CrowCast.Observability.Models;

public sealed record CrowCastObservationSnapshot(
    int SchemaVersion,
    CrowCastClusterKey Cluster,
    CrowCastObserverIdentity Observer,
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    CrowCastObserverIdentity? Coordinator,
    IReadOnlyList<CrowCastMemberObservation> Members,
    CrowCastTelemetryCounters Counters);
