using System.Text.Json;
using System.Text.Json.Serialization;
using Akka.Actor;
using Akka.Serialization;
using CrowCast.Akka.Transport;

namespace CrowCast.Akka.Serialization;

public sealed class CrowCastAkkaSerializer : SerializerWithStringManifest
{
    private const string NodeAnnouncementManifest = "crowcast-node-v1";
    private const string NodeLeavingManifest = "crowcast-leave-v1";
    private const string MessageManifest = "crowcast-message-v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public CrowCastAkkaSerializer(ExtendedActorSystem system)
        : base(system)
    {
    }

    public override int Identifier => 771;

    public override string Manifest(object value) => value switch
    {
        CrowCastNodeAnnouncement => NodeAnnouncementManifest,
        CrowCastNodeLeaving => NodeLeavingManifest,
        CrowCastMessageEnvelope => MessageManifest,
        _ => throw new ArgumentException($"Unsupported CrowCast wire type {value.GetType()}.", nameof(value))
    };

    public override byte[] ToBinary(object obj) => obj switch
    {
        CrowCastNodeAnnouncement announcement => JsonSerializer.SerializeToUtf8Bytes(announcement, SerializerOptions),
        CrowCastNodeLeaving leaving => JsonSerializer.SerializeToUtf8Bytes(leaving, SerializerOptions),
        CrowCastMessageEnvelope message => JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions),
        _ => throw new ArgumentException($"Unsupported CrowCast wire type {obj.GetType()}.", nameof(obj))
    };

    public override object FromBinary(byte[] bytes, string manifest) => manifest switch
    {
        NodeAnnouncementManifest => Deserialize<CrowCastNodeAnnouncement>(bytes, manifest),
        NodeLeavingManifest => Deserialize<CrowCastNodeLeaving>(bytes, manifest),
        MessageManifest => Deserialize<CrowCastMessageEnvelope>(bytes, manifest),
        _ => throw new ArgumentException($"Unsupported CrowCast manifest '{manifest}'.", nameof(manifest))
    };

    private static T Deserialize<T>(byte[] bytes, string manifest) =>
        JsonSerializer.Deserialize<T>(bytes, SerializerOptions) ??
        throw new JsonException($"CrowCast payload '{manifest}' could not be deserialized.");
}
