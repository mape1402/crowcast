using CrowCast.Diagnostics;

namespace CrowCast.Observability.Models;

public sealed record CrowCastTelemetryCounters(
    long MessagesPublished,
    long MessagesReceived,
    long DuplicateMessages,
    long ExpiredMessages,
    long RejectedMessages,
    long RateLimitedMessages,
    long RejectedMembershipUpdates,
    long MembershipChanges,
    long CoordinatorChanges)
{
    public static CrowCastTelemetryCounters From(CrowCastDiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot.MessagesPublished,
            snapshot.MessagesReceived,
            snapshot.DuplicateMessages,
            snapshot.ExpiredMessages,
            snapshot.RejectedMessages,
            snapshot.RateLimitedMessages,
            snapshot.RejectedMembershipUpdates,
            snapshot.MembershipChanges,
            snapshot.CoordinatorChanges);
    }
}
