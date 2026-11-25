using CrowCast.Membership;

namespace CrowCast.Backends;

public sealed record CrowCastBackendMemberChanged(CrowCastMember Member) : CrowCastBackendNotification;
