using CrowCast.Membership;

namespace CrowCast.Messaging;

public sealed record CrowCastMessageTarget
{
    private CrowCastMessageTarget(CrowCastNodeIdentity? member)
    {
        Member = member;
    }

    public static CrowCastMessageTarget AllMembers { get; } = new((CrowCastNodeIdentity?)null);

    public CrowCastNodeIdentity? Member { get; }

    public bool IsAllMembers => Member is null;

    public static CrowCastMessageTarget ForMember(CrowCastNodeIdentity member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return new(member);
    }
}
