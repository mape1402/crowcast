namespace CrowCast.Observability.Models;

public sealed record CrowCastMemberObservation(
    CrowCastObserverIdentity Identity,
    string Status,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastChangedAtUtc,
    string? Endpoint,
    IReadOnlyDictionary<string, string> Metadata,
    bool IsCoordinator,
    bool IsLocal);
