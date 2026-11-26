using CrowCast.Observability.Models;

namespace CrowCast.ControlPlane.Models;

public sealed record CrowCastClusterView(
    CrowCastClusterKey Key,
    string Health,
    int Observers,
    int FreshObservers,
    int AliveMembers,
    int SuspectMembers,
    int DeadMembers,
    int LeftMembers,
    int CoordinatorCount,
    bool MembershipDisagreement,
    DateTimeOffset LastSeenAtUtc,
    IReadOnlyList<CrowCastObserverView> ObserverViews);
