using CrowCast.ControlPlane.Protocol.V1;
using CrowCast.Observability.Models;

namespace CrowCast.ControlPlane.Client.Internal;

internal static class CrowCastProtocolMapper
{
    public static ClusterKey Map(CrowCastClusterKey value) => new()
    {
        Environment = value.Environment,
        Service = value.Service,
        Cluster = value.Cluster,
        Scope = value.Scope
    };

    public static ObserverIdentity Map(CrowCastObserverIdentity value) => new()
    {
        NodeId = value.NodeId,
        Generation = value.Generation
    };

    public static Snapshot Map(CrowCastObservationSnapshot value)
    {
        var result = new Snapshot
        {
            SchemaVersion = value.SchemaVersion,
            Cluster = Map(value.Cluster),
            Observer = Map(value.Observer),
            ObservationSequence = value.Sequence,
            ObservedUnixMs = value.ObservedAtUtc.ToUnixTimeMilliseconds(),
            Counters = Map(value.Counters)
        };
        if (value.Coordinator is not null)
        {
            result.Coordinator = Map(value.Coordinator);
        }

        result.Members.AddRange(value.Members.Select(Map));
        return result;
    }

    public static Protocol.V1.ObservationEvent Map(Observability.Models.CrowCastObservationEvent value)
    {
        var result = new Protocol.V1.ObservationEvent
        {
            Sequence = value.Sequence,
            OccurredUnixMs = value.OccurredAtUtc.ToUnixTimeMilliseconds(),
            Category = value.Category,
            Summary = value.Summary,
            PreviousStatus = value.PreviousStatus ?? string.Empty,
            CurrentStatus = value.CurrentStatus ?? string.Empty,
            MessageType = value.MessageType ?? string.Empty
        };
        if (value.Subject is not null)
        {
            result.Subject = Map(value.Subject);
        }

        return result;
    }

    private static MemberObservation Map(CrowCastMemberObservation value)
    {
        var result = new MemberObservation
        {
            Identity = Map(value.Identity),
            Status = value.Status,
            FirstSeenUnixMs = value.FirstSeenAtUtc.ToUnixTimeMilliseconds(),
            LastChangedUnixMs = value.LastChangedAtUtc.ToUnixTimeMilliseconds(),
            Endpoint = value.Endpoint ?? string.Empty,
            IsCoordinator = value.IsCoordinator,
            IsLocal = value.IsLocal
        };
        foreach (var pair in value.Metadata)
        {
            result.Metadata.Add(pair.Key, pair.Value);
        }
        return result;
    }

    private static TelemetryCounters Map(CrowCastTelemetryCounters value) => new()
    {
        MessagesPublished = value.MessagesPublished,
        MessagesReceived = value.MessagesReceived,
        DuplicateMessages = value.DuplicateMessages,
        ExpiredMessages = value.ExpiredMessages,
        RejectedMessages = value.RejectedMessages,
        RateLimitedMessages = value.RateLimitedMessages,
        RejectedMembershipUpdates = value.RejectedMembershipUpdates,
        MembershipChanges = value.MembershipChanges,
        CoordinatorChanges = value.CoordinatorChanges
    };
}
