using System.Diagnostics.Metrics;
using CrowCast.Diagnostics;
using CrowCast.Membership;

namespace CrowCast.Internal;

internal sealed class CrowCastTelemetry : IDisposable
{
    private readonly Func<IReadOnlyCollection<CrowCastMember>> _members;
    private readonly Meter _meter = new("Elysium.CrowCast", "0.1.0");
    private readonly Counter<long> _messagesPublished;
    private readonly Counter<long> _messagesReceived;
    private readonly Counter<long> _duplicateMessages;
    private readonly Counter<long> _expiredMessages;
    private readonly Counter<long> _rejectedMessages;
    private readonly Counter<long> _rejectedMembershipUpdates;
    private readonly Counter<long> _membershipChanges;
    private readonly Counter<long> _coordinatorChanges;
    private long _publishedCount;
    private long _receivedCount;
    private long _duplicateCount;
    private long _expiredCount;
    private long _rejectedCount;
    private long _rateLimitedCount;
    private long _rejectedMembershipUpdateCount;
    private long _membershipChangeCount;
    private long _coordinatorChangeCount;

    public CrowCastTelemetry(Func<IReadOnlyCollection<CrowCastMember>> members)
    {
        _members = members;
        _messagesPublished = _meter.CreateCounter<long>("crowcast.messages.published");
        _messagesReceived = _meter.CreateCounter<long>("crowcast.messages.received");
        _duplicateMessages = _meter.CreateCounter<long>("crowcast.messages.duplicates");
        _expiredMessages = _meter.CreateCounter<long>("crowcast.messages.expired");
        _rejectedMessages = _meter.CreateCounter<long>("crowcast.messages.rejected");
        _rejectedMembershipUpdates = _meter.CreateCounter<long>("crowcast.membership.rejected");
        _membershipChanges = _meter.CreateCounter<long>("crowcast.membership.changes");
        _coordinatorChanges = _meter.CreateCounter<long>("crowcast.coordinator.changes");
        _meter.CreateObservableGauge("crowcast.members", ObserveMembers);
    }

    public void RecordPublished()
    {
        Interlocked.Increment(ref _publishedCount);
        _messagesPublished.Add(1);
    }

    public void RecordReceived()
    {
        Interlocked.Increment(ref _receivedCount);
        _messagesReceived.Add(1);
    }

    public void RecordDuplicate()
    {
        Interlocked.Increment(ref _duplicateCount);
        _duplicateMessages.Add(1);
    }

    public void RecordExpired()
    {
        Interlocked.Increment(ref _expiredCount);
        _expiredMessages.Add(1);
    }

    public void RecordRejectedMessage(string reason, bool rateLimited = false)
    {
        Interlocked.Increment(ref _rejectedCount);
        if (rateLimited)
        {
            Interlocked.Increment(ref _rateLimitedCount);
        }

        _rejectedMessages.Add(1, new KeyValuePair<string, object?>("crowcast.rejection.reason", reason));
    }

    public void RecordRejectedMembershipUpdate(string reason)
    {
        Interlocked.Increment(ref _rejectedMembershipUpdateCount);
        _rejectedMembershipUpdates.Add(
            1,
            new KeyValuePair<string, object?>("crowcast.rejection.reason", reason));
    }

    public void RecordMembershipChange(CrowCastMemberStatus status)
    {
        Interlocked.Increment(ref _membershipChangeCount);
        _membershipChanges.Add(1, new KeyValuePair<string, object?>("crowcast.member.status", status.ToString()));
    }

    public void RecordCoordinatorChange()
    {
        Interlocked.Increment(ref _coordinatorChangeCount);
        _coordinatorChanges.Add(1);
    }

    public CrowCastDiagnosticsSnapshot Snapshot(
        CrowCastNodeIdentity localNode,
        CrowCastNodeIdentity? coordinator)
    {
        var members = _members();
        return new CrowCastDiagnosticsSnapshot(
            localNode,
            coordinator,
            members.Count(member => member.Status == CrowCastMemberStatus.Alive),
            members.Count(member => member.Status == CrowCastMemberStatus.Suspect),
            members.Count(member => member.Status == CrowCastMemberStatus.Dead),
            members.Count(member => member.Status == CrowCastMemberStatus.Left),
            Interlocked.Read(ref _publishedCount),
            Interlocked.Read(ref _receivedCount),
            Interlocked.Read(ref _duplicateCount),
            Interlocked.Read(ref _expiredCount),
            Interlocked.Read(ref _rejectedCount),
            Interlocked.Read(ref _rateLimitedCount),
            Interlocked.Read(ref _rejectedMembershipUpdateCount),
            Interlocked.Read(ref _membershipChangeCount),
            Interlocked.Read(ref _coordinatorChangeCount));
    }

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveMembers()
    {
        var members = _members();
        foreach (var status in Enum.GetValues<CrowCastMemberStatus>())
        {
            yield return new Measurement<long>(
                members.LongCount(member => member.Status == status),
                new KeyValuePair<string, object?>("crowcast.member.status", status.ToString()));
        }
    }
}
