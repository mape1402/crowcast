using CrowCast.Membership;

namespace CrowCast.Events;

public sealed record CrowCastCoordinatorChanged(
    CrowCastNodeIdentity? Previous,
    CrowCastNodeIdentity? Current,
    DateTimeOffset OccurredAtUtc) : CrowCastEvent(OccurredAtUtc);
