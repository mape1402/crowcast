using System.Text.Json;

namespace CrowCast.Messaging;

public sealed class SystemTextJsonCrowCastMessageSerializer : ICrowCastMessageSerializer
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonCrowCastMessageSerializer()
        : this(new JsonSerializerOptions(JsonSerializerDefaults.Web))
    {
    }

    public SystemTextJsonCrowCastMessageSerializer(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public byte[] Serialize<TMessage>(TMessage message)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.SerializeToUtf8Bytes(message, _options);
    }

    public TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> content)
        where TMessage : notnull =>
        JsonSerializer.Deserialize<TMessage>(content.Span, _options) ??
        throw new JsonException($"CrowCast payload {typeof(TMessage)} deserialized to null.");
}
