using System.Collections.ObjectModel;

namespace CrowCast.Membership;

public sealed record CrowCastMember
{
    public CrowCastMember(
        CrowCastNodeIdentity identity,
        CrowCastMemberStatus status,
        DateTimeOffset firstSeenAtUtc,
        DateTimeOffset lastChangedAtUtc,
        string? endpoint = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Status = status;
        FirstSeenAtUtc = firstSeenAtUtc;
        LastChangedAtUtc = lastChangedAtUtc;
        Endpoint = endpoint;
        Metadata = CopyMetadata(metadata);
    }

    public CrowCastNodeIdentity Identity { get; }

    public CrowCastMemberStatus Status { get; }

    public DateTimeOffset FirstSeenAtUtc { get; }

    public DateTimeOffset LastChangedAtUtc { get; }

    public string? Endpoint { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        var copy = metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
