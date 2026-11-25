using System.Text;
using CrowCast.Configuration;
using CrowCast.Membership;
using CrowCast.Messaging;

namespace CrowCast.Internal;

internal static class CrowCastInputValidator
{
    public static bool TryValidateMessageShape(
        CrowCastMessage message,
        CrowCastOptions options,
        out string reason)
    {
        if (Utf8Length(message.Type) > options.MaximumMessageTypeBytes)
        {
            reason = "type_too_large";
            return false;
        }

        if (message.Content.Length > options.MaximumMessageContentBytes)
        {
            reason = "content_too_large";
            return false;
        }

        return TryValidateMetadata(message.Metadata, options, out reason);
    }

    public static bool TryValidateMember(
        CrowCastMember member,
        CrowCastOptions options,
        DateTimeOffset now,
        out string reason)
    {
        if (!Enum.IsDefined(member.Status))
        {
            reason = "invalid_status";
            return false;
        }

        if (member.Endpoint is not null &&
            (string.IsNullOrWhiteSpace(member.Endpoint) ||
             Utf8Length(member.Endpoint) > options.MaximumEndpointBytes))
        {
            reason = "invalid_endpoint";
            return false;
        }

        if (member.FirstSeenAtUtc - now > options.MaximumClockSkew ||
            member.LastChangedAtUtc - now > options.MaximumClockSkew)
        {
            reason = "timestamp_too_far_in_future";
            return false;
        }

        if (member.FirstSeenAtUtc - member.LastChangedAtUtc > options.MaximumClockSkew)
        {
            reason = "invalid_timestamp_order";
            return false;
        }

        return TryValidateMetadata(member.Metadata, options, out reason);
    }

    public static bool IsTooFarInFuture(
        CrowCastMessage message,
        CrowCastOptions options,
        DateTimeOffset now) =>
        message.CreatedAtUtc - now > options.MaximumClockSkew;

    public static bool IsExpired(
        CrowCastMessage message,
        CrowCastOptions options,
        DateTimeOffset now) =>
        now - message.CreatedAtUtc >= options.MessageTimeToLive;

    private static bool TryValidateMetadata(
        IReadOnlyDictionary<string, string> metadata,
        CrowCastOptions options,
        out string reason)
    {
        if (metadata.Count > options.MaximumMetadataEntries)
        {
            reason = "too_many_metadata_entries";
            return false;
        }

        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                reason = "invalid_metadata_key";
                return false;
            }

            if (Utf8Length(pair.Key) > options.MaximumMetadataKeyBytes)
            {
                reason = "metadata_key_too_large";
                return false;
            }

            if (pair.Value is null)
            {
                reason = "null_metadata_value";
                return false;
            }

            if (Utf8Length(pair.Value) > options.MaximumMetadataValueBytes)
            {
                reason = "metadata_value_too_large";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static int Utf8Length(string value) => Encoding.UTF8.GetByteCount(value);
}
