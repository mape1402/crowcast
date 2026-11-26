using CrowCast.Observability.Models;

namespace CrowCast.ControlPlane.Models;

public sealed record CrowCastObserverView(
    CrowCastObserverIdentity Identity,
    CrowCastObservationSnapshot? Snapshot,
    DateTimeOffset LastSeenAtUtc,
    bool Connected,
    string Freshness);
