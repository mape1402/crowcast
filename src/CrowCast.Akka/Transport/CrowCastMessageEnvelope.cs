namespace CrowCast.Akka.Transport;

internal sealed record CrowCastMessageEnvelope(
    Guid MessageId,
    string Type,
    byte[] Content,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyDictionary<string, string> Metadata,
    Guid? OriginNodeId,
    Guid? OriginGeneration) : ICrowCastWireMessage;
