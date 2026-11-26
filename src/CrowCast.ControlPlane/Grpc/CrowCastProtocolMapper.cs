using CrowCast.ControlPlane.Protocol.V1;
using CrowCast.Observability.Models;

namespace CrowCast.ControlPlane.Grpc;

internal static class CrowCastProtocolMapper
{
    public static CrowCastClusterKey Map(ClusterKey value) => new(value.Environment, value.Service, value.Cluster, value.Scope);

    public static CrowCastObserverIdentity Map(ObserverIdentity value) => new(value.NodeId, value.Generation);

    public static CrowCastObservationSnapshot Map(Snapshot value) => new(
        value.SchemaVersion,
        Map(value.Cluster),
        Map(value.Observer),
        value.ObservationSequence,
        DateTimeOffset.FromUnixTimeMilliseconds(value.ObservedUnixMs),
        value.Coordinator is null ? null : Map(value.Coordinator),
        value.Members.Select(Map).ToArray(),
        Map(value.Counters));

    public static CrowCastObservationEvent Map(Protocol.V1.ObservationEvent value) => new(
        value.Sequence,
        DateTimeOffset.FromUnixTimeMilliseconds(value.OccurredUnixMs),
        value.Category,
        value.Summary,
        value.Subject is null ? null : Map(value.Subject),
        EmptyToNull(value.PreviousStatus),
        EmptyToNull(value.CurrentStatus),
        EmptyToNull(value.MessageType));

    private static CrowCastMemberObservation Map(MemberObservation value) => new(
        Map(value.Identity),
        value.Status,
        DateTimeOffset.FromUnixTimeMilliseconds(value.FirstSeenUnixMs),
        DateTimeOffset.FromUnixTimeMilliseconds(value.LastChangedUnixMs),
        EmptyToNull(value.Endpoint),
        new Dictionary<string, string>(value.Metadata, StringComparer.Ordinal),
        value.IsCoordinator,
        value.IsLocal);

    private static CrowCastTelemetryCounters Map(TelemetryCounters value) => new(
        value.MessagesPublished,
        value.MessagesReceived,
        value.DuplicateMessages,
        value.ExpiredMessages,
        value.RejectedMessages,
        value.RateLimitedMessages,
        value.RejectedMembershipUpdates,
        value.MembershipChanges,
        value.CoordinatorChanges);

    private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;
}
