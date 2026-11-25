namespace CrowCast.Akka.Transport;

internal sealed record CrowCastNodeAnnouncement(
    Guid NodeId,
    Guid Generation,
    string Endpoint,
    DateTimeOffset StartedAtUtc,
    IReadOnlyDictionary<string, string> Metadata) : ICrowCastWireMessage;
