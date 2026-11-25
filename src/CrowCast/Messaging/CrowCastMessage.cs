using System.Collections.ObjectModel;
using CrowCast.Membership;

namespace CrowCast.Messaging;

public sealed record CrowCastMessage
{
    public CrowCastMessage(
        CrowCastMessageId id,
        string type,
        ReadOnlyMemory<byte> content,
        DateTimeOffset createdAtUtc,
        IReadOnlyDictionary<string, string>? metadata = null,
        CrowCastNodeIdentity? origin = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("A message type is required.", nameof(type));
        }

        Id = id;
        Type = type.Trim();
        Content = content.ToArray();
        CreatedAtUtc = createdAtUtc;
        Metadata = CopyMetadata(metadata);
        Origin = origin;
    }

    public CrowCastMessageId Id { get; }

    public string Type { get; }

    public ReadOnlyMemory<byte> Content { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public CrowCastNodeIdentity? Origin { get; }

    public static CrowCastMessage Create(
        string type,
        ReadOnlyMemory<byte> content,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(CrowCastMessageId.New(), type, content, DateTimeOffset.UtcNow, metadata);

    internal CrowCastMessage WithOrigin(CrowCastNodeIdentity origin) =>
        new(Id, Type, Content, CreatedAtUtc, Metadata, origin);

    private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        var copy = metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
