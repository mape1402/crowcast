namespace CrowCast.Messaging;

public readonly record struct CrowCastMessageId
{
    public CrowCastMessageId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A message identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static CrowCastMessageId New() => new(Guid.NewGuid());

    public static CrowCastMessageId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("N");
}
