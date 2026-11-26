using CrowCast.Membership;

namespace CrowCast.Observability.Models;

public sealed record CrowCastObserverIdentity(string NodeId, string Generation)
{
    public static CrowCastObserverIdentity From(CrowCastNodeIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new(identity.NodeId.ToString(), identity.Generation.ToString());
    }

    public override string ToString() => $"{NodeId}/{Generation}";
}
