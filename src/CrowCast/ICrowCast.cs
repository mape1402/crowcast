using CrowCast.Membership;
using CrowCast.Messaging;

namespace CrowCast;

public interface ICrowCast
{
    CrowCastNodeIdentity LocalNode { get; }

    IReadOnlyCollection<CrowCastMember> Members { get; }

    CrowCastNodeIdentity? Coordinator { get; }

    ValueTask PublishAsync(CrowCastMessage message, CancellationToken cancellationToken = default);

    ValueTask PublishToAllAsync(CrowCastMessage message, CancellationToken cancellationToken = default);

    ValueTask PublishToMemberAsync(
        CrowCastMessage message,
        CrowCastNodeIdentity member,
        CancellationToken cancellationToken = default);

    ValueTask PublishToCoordinatorAsync(
        CrowCastMessage message,
        CancellationToken cancellationToken = default);
}
