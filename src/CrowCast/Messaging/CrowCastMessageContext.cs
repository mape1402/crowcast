using CrowCast.Membership;

namespace CrowCast.Messaging;

public sealed record CrowCastMessageContext<TMessage>
    where TMessage : notnull
{
    public CrowCastMessageContext(
        TMessage message,
        CrowCastMessage envelope,
        DateTimeOffset occurredAtUtc,
        ICrowCast crowCast)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(crowCast);

        Message = message;
        Envelope = envelope;
        OccurredAtUtc = occurredAtUtc;
        CrowCast = crowCast;
    }

    public TMessage Message { get; }

    public CrowCastMessage Envelope { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public ICrowCast CrowCast { get; }

    public CrowCastNodeIdentity? Origin => Envelope.Origin;

    public IReadOnlyDictionary<string, string> Metadata => Envelope.Metadata;
}
