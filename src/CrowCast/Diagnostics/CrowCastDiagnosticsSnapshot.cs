using CrowCast.Membership;

namespace CrowCast.Diagnostics;

public sealed record CrowCastDiagnosticsSnapshot(
    CrowCastNodeIdentity LocalNode,
    CrowCastNodeIdentity? Coordinator,
    int AliveMembers,
    int SuspectMembers,
    int DeadMembers,
    int LeftMembers,
    long MessagesPublished,
    long MessagesReceived,
    long DuplicateMessages,
    long ExpiredMessages,
    long RejectedMessages,
    long RateLimitedMessages,
    long RejectedMembershipUpdates,
    long MembershipChanges,
    long CoordinatorChanges);
