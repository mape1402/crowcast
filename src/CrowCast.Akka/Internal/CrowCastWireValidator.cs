using System.Text;
using CrowCast.Akka.Transport;
using CrowCast.Backends;

namespace CrowCast.Akka.Internal;

internal static class CrowCastWireValidator
{
    public static bool TryValidate(
        CrowCastNodeAnnouncement announcement,
        CrowCastProtocolLimits limits,
        DateTimeOffset now,
        out string reason) =>
        TryValidateNode(
            announcement.NodeId,
            announcement.Generation,
            announcement.Endpoint,
            announcement.StartedAtUtc,
            announcement.Metadata,
            limits,
            now,
            out reason);

    public static bool TryValidate(
        CrowCastNodeLeaving leaving,
        CrowCastProtocolLimits limits,
        DateTimeOffset now,
        out string reason) =>
        TryValidateNode(
            leaving.NodeId,
            leaving.Generation,
            leaving.Endpoint,
            leaving.LeftAtUtc,
            leaving.Metadata,
            limits,
            now,
            out reason);

    public static bool TryValidate(CrowCastMessageEnvelope envelope, out string reason)
    {
        if (envelope.MessageId == Guid.Empty)
        {
            reason = "empty_message_id";
            return false;
        }

        if (string.IsNullOrWhiteSpace(envelope.Type))
        {
            reason = "invalid_message_type";
            return false;
        }

        if (envelope.Content is null || envelope.Metadata is null)
        {
            reason = "missing_message_data";
            return false;
        }

        if (envelope.OriginNodeId.HasValue != envelope.OriginGeneration.HasValue ||
            envelope.OriginNodeId == Guid.Empty ||
            envelope.OriginGeneration == Guid.Empty)
        {
            reason = "invalid_message_origin";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryValidateNode(
        Guid nodeId,
        Guid generation,
        string endpoint,
        DateTimeOffset timestamp,
        IReadOnlyDictionary<string, string> metadata,
        CrowCastProtocolLimits limits,
        DateTimeOffset now,
        out string reason)
    {
        if (nodeId == Guid.Empty || generation == Guid.Empty)
        {
            reason = "empty_node_identity";
            return false;
        }

        if (string.IsNullOrWhiteSpace(endpoint) || Utf8Length(endpoint) > limits.MaximumEndpointBytes)
        {
            reason = "invalid_endpoint";
            return false;
        }

        if (timestamp - now > limits.MaximumClockSkew)
        {
            reason = "timestamp_too_far_in_future";
            return false;
        }

        if (!TryValidateMetadata(metadata, limits, out reason))
        {
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryValidateMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        CrowCastProtocolLimits limits,
        out string reason)
    {
        if (metadata is null || metadata.Count > limits.MaximumMetadataEntries)
        {
            reason = "invalid_metadata_count";
            return false;
        }

        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) ||
                Utf8Length(pair.Key) > limits.MaximumMetadataKeyBytes ||
                pair.Value is null ||
                Utf8Length(pair.Value) > limits.MaximumMetadataValueBytes)
            {
                reason = "invalid_metadata_entry";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static int Utf8Length(string value) => Encoding.UTF8.GetByteCount(value);
}
