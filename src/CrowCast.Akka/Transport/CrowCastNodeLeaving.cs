namespace CrowCast.Akka.Transport;

internal sealed record CrowCastNodeLeaving(
    Guid NodeId,
    Guid Generation,
    string Endpoint,
    DateTimeOffset LeftAtUtc,
    IReadOnlyDictionary<string, string> Metadata) : ICrowCastWireMessage;
