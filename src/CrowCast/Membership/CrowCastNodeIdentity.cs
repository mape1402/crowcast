namespace CrowCast.Membership;

public sealed record CrowCastNodeIdentity(CrowCastNodeId NodeId, CrowCastNodeGeneration Generation)
    : IComparable<CrowCastNodeIdentity>
{
    public static CrowCastNodeIdentity New() => new(CrowCastNodeId.New(), CrowCastNodeGeneration.New());

    public int CompareTo(CrowCastNodeIdentity? other)
    {
        if (other is null)
        {
            return 1;
        }

        var nodeComparison = NodeId.CompareTo(other.NodeId);
        return nodeComparison != 0 ? nodeComparison : Generation.CompareTo(other.Generation);
    }

    public override string ToString() => $"{NodeId}/{Generation}";
}
