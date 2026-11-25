using CrowCast.Membership;

namespace CrowCast.Events;

public sealed record CrowCastMemberChanged(
    CrowCastMember? Previous,
    CrowCastMember Current,
    DateTimeOffset OccurredAtUtc) : CrowCastEvent(OccurredAtUtc);
