namespace CrowCast.Membership;

public readonly record struct CrowCastNodeGeneration : IComparable<CrowCastNodeGeneration>
{
    public CrowCastNodeGeneration(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A node generation cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static CrowCastNodeGeneration New() => new(Guid.NewGuid());

    public static CrowCastNodeGeneration Parse(string value) => new(Guid.Parse(value));

    public int CompareTo(CrowCastNodeGeneration other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("N");
}
