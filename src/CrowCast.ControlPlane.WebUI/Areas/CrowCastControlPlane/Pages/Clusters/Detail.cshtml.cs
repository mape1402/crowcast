using CrowCast.ControlPlane.Models;
using CrowCast.ControlPlane.Storage;
using CrowCast.Observability.Models;
using Microsoft.AspNetCore.Mvc;

namespace CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages.Clusters;

public sealed class DetailModel(
    ICrowCastControlPlaneStore store,
    TimeProvider timeProvider)
    : ControlPlanePageModel(store, timeProvider)
{
    public CrowCastClusterView ClusterView { get; private set; } = null!;
    public CrowCastObserverIdentity? Coordinator { get; private set; }
    public CrowCastTelemetryCounters Counters { get; private set; } = EmptyCounters;

    private static CrowCastTelemetryCounters EmptyCounters => new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    public IActionResult OnGet(string environment, string service, string cluster, string scope = "")
    {
        var now = TimeProvider.GetUtcNow();
        var view = Store.GetCluster(Key(environment, service, cluster, scope), now);
        if (view is null) return NotFound();

        ClusterView = view;
        var snapshots = Snapshots(view);
        Coordinator = ConsensusCoordinator(snapshots);
        Counters = AggregateCounters(snapshots);
        return Page();
    }

    public IActionResult OnGetObservers(string environment, string service, string cluster, string scope = "")
    {
        var view = Store.GetCluster(Key(environment, service, cluster, scope), TimeProvider.GetUtcNow());
        return new JsonResult(view?.ObserverViews.Select(observer => new
        {
            id = observer.Identity.ToString(),
            observer.Identity.NodeId,
            observer.Identity.Generation,
            observer.Freshness,
            observer.Connected,
            members = observer.Snapshot?.Members.Count ?? 0,
            coordinator = observer.Snapshot?.Coordinator?.NodeId,
            observer.LastSeenAtUtc
        }) ?? []);
    }

    public IActionResult OnGetData(string environment, string service, string cluster, string scope = "")
    {
        var view = Store.GetCluster(Key(environment, service, cluster, scope), TimeProvider.GetUtcNow());
        var snapshots = Snapshots(view);
        var coordinator = ConsensusCoordinator(snapshots);
        return new JsonResult(new
        {
            members = AggregateMembers(snapshots, coordinator).Select(member => member.Member),
            observers = view?.ObserverViews ?? [],
            counters = AggregateCounters(snapshots),
            coordinator,
            observedAtUtc = snapshots.Length == 0 ? (DateTimeOffset?)null : snapshots.Max(snapshot => snapshot.ObservedAtUtc)
        });
    }

    public IActionResult OnGetMembers(string environment, string service, string cluster, string scope = "")
    {
        var snapshots = Snapshots(Store.GetCluster(Key(environment, service, cluster, scope), TimeProvider.GetUtcNow()));
        var members = AggregateMembers(snapshots, ConsensusCoordinator(snapshots));
        return new JsonResult(members.Select(item => new
        {
            id = item.Member.Identity.ToString(),
            item.Member.Identity.NodeId,
            item.Member.Identity.Generation,
            item.Member.Status,
            coordinator = item.Member.IsCoordinator ? "Yes" : string.Empty,
            seenBy = item.SeenBy,
            observers = item.ObserverCount,
            agreement = item.StatusAgreement ? "In agreement" : "Disagreement",
            item.Member.FirstSeenAtUtc,
            item.Member.LastChangedAtUtc,
            item.Member.Endpoint
        }));
    }

    public IActionResult OnGetEvents(string environment, string service, string cluster, string scope = "") =>
        new JsonResult(Store.GetEvents(Key(environment, service, cluster, scope), take: 1_000).Select((item, index) => new
        {
            id = $"{item.Sequence}:{index}",
            item.OccurredAtUtc,
            item.Category,
            item.Summary,
            item.PreviousStatus,
            item.CurrentStatus,
            item.MessageType
        }));

    private static CrowCastClusterKey Key(string environment, string service, string cluster, string scope) =>
        new(environment ?? string.Empty, service ?? string.Empty, cluster ?? string.Empty, scope ?? string.Empty);

    private static CrowCastObservationSnapshot[] Snapshots(CrowCastClusterView? view)
    {
        var available = view?.ObserverViews.Where(observer => observer.Snapshot is not null)
            .Select(observer => observer.Snapshot!).ToArray() ?? [];
        var fresh = view?.ObserverViews.Where(observer => observer.Freshness == "Fresh" && observer.Snapshot is not null)
            .Select(observer => observer.Snapshot!).ToArray() ?? [];
        return fresh.Length > 0 ? fresh : available;
    }

    private static CrowCastObserverIdentity? ConsensusCoordinator(IReadOnlyCollection<CrowCastObservationSnapshot> snapshots) => snapshots
        .Where(snapshot => snapshot.Coordinator is not null)
        .Select(snapshot => snapshot.Coordinator!)
        .GroupBy(identity => identity)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key.ToString(), StringComparer.Ordinal)
        .Select(group => group.Key)
        .FirstOrDefault();

    private static CrowCastAggregatedMember[] AggregateMembers(
        IReadOnlyCollection<CrowCastObservationSnapshot> snapshots,
        CrowCastObserverIdentity? coordinator) => snapshots.SelectMany(snapshot => snapshot.Members)
        .GroupBy(member => member.Identity)
        .Select(group =>
        {
            var statusGroups = group.GroupBy(member => member.Status).ToArray();
            var status = statusGroups.OrderByDescending(statusGroup => statusGroup.Count())
                .ThenBy(statusGroup => statusGroup.Key, StringComparer.Ordinal).First().Key;
            var source = group.OrderBy(member => member.Endpoint, StringComparer.Ordinal)
                .ThenBy(member => member.Status, StringComparer.Ordinal).First();
            var member = new CrowCastMemberObservation(
                group.Key,
                status,
                group.Min(item => item.FirstSeenAtUtc),
                group.Max(item => item.LastChangedAtUtc),
                group.Select(item => item.Endpoint).Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
                    .Order(StringComparer.Ordinal).FirstOrDefault(),
                source.Metadata,
                group.Key == coordinator,
                false);
            return new CrowCastAggregatedMember(member, group.Count(), snapshots.Count, statusGroups.Length == 1);
        })
        .OrderBy(member => member.Member.Identity.NodeId, StringComparer.Ordinal)
        .ThenBy(member => member.Member.Identity.Generation, StringComparer.Ordinal)
        .ToArray();

    private static CrowCastTelemetryCounters AggregateCounters(IEnumerable<CrowCastObservationSnapshot> snapshots)
    {
        var counters = EmptyCounters;
        foreach (var snapshot in snapshots)
        {
            var current = snapshot.Counters;
            counters = new(
                counters.MessagesPublished + current.MessagesPublished,
                counters.MessagesReceived + current.MessagesReceived,
                counters.DuplicateMessages + current.DuplicateMessages,
                counters.ExpiredMessages + current.ExpiredMessages,
                counters.RejectedMessages + current.RejectedMessages,
                counters.RateLimitedMessages + current.RateLimitedMessages,
                counters.RejectedMembershipUpdates + current.RejectedMembershipUpdates,
                counters.MembershipChanges + current.MembershipChanges,
                counters.CoordinatorChanges + current.CoordinatorChanges);
        }

        return counters;
    }
}

internal sealed record CrowCastAggregatedMember(
    CrowCastMemberObservation Member,
    int SeenBy,
    int ObserverCount,
    bool StatusAgreement);
