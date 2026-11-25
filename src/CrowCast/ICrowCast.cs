using CrowCast.Membership;
using CrowCast.Messaging;

namespace CrowCast;

public interface ICrowCast
{
    CrowCastNodeIdentity LocalNode { get; }

    IReadOnlyCollection<CrowCastMember> Members { get; }

    CrowCastNodeIdentity? Coordinator { get; }

    ValueTask PublishAsync(CrowCastMessage message, CancellationToken cancellationToken = default);
}
