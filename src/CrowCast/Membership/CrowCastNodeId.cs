namespace CrowCast.Membership;

public readonly record struct CrowCastNodeId : IComparable<CrowCastNodeId>
{
    public CrowCastNodeId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A node identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static CrowCastNodeId New() => new(Guid.NewGuid());

    public static CrowCastNodeId Parse(string value) => new(Guid.Parse(value));

    public int CompareTo(CrowCastNodeId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("N");
}
