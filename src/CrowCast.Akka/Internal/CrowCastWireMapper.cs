using CrowCast.Akka.Transport;
using CrowCast.Membership;
using CrowCast.Messaging;

namespace CrowCast.Akka.Internal;

internal static class CrowCastWireMapper
{
    public static CrowCastMessageEnvelope ToEnvelope(CrowCastMessage message) =>
        new(
            message.Id.Value,
            message.Type,
            message.Content.ToArray(),
            message.CreatedAtUtc,
            message.Metadata,
            message.Origin?.NodeId.Value,
            message.Origin?.Generation.Value);

    public static CrowCastMessage ToMessage(CrowCastMessageEnvelope envelope)
    {
        CrowCastNodeIdentity? origin = null;
        if (envelope.OriginNodeId.HasValue && envelope.OriginGeneration.HasValue)
        {
            origin = new CrowCastNodeIdentity(
                new CrowCastNodeId(envelope.OriginNodeId.Value),
                new CrowCastNodeGeneration(envelope.OriginGeneration.Value));
        }

        return new CrowCastMessage(
            new CrowCastMessageId(envelope.MessageId),
            envelope.Type,
            envelope.Content,
            envelope.CreatedAtUtc,
            envelope.Metadata,
            origin);
    }
}
