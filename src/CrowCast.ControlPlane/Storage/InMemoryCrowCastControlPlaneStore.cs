using CrowCast.ControlPlane.Configuration;
using CrowCast.ControlPlane.Models;
using CrowCast.Observability.Models;
using Microsoft.Extensions.Options;

namespace CrowCast.ControlPlane.Storage;

internal sealed class InMemoryCrowCastControlPlaneStore : ICrowCastControlPlaneStore
{
    private readonly CrowCastControlPlaneOptions _options;
    private readonly Dictionary<string, ClusterState> _clusters = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public InMemoryCrowCastControlPlaneStore(IOptions<CrowCastControlPlaneOptions> options) => _options = options.Value;

    public event Action<CrowCastClusterKey>? Changed;

    public void Connect(CrowCastClusterKey cluster, CrowCastObserverIdentity observer, DateTimeOffset observedAtUtc) =>
        Mutate(cluster, state => GetObserver(state, observer, observedAtUtc).Connected = true);

    public void Upsert(CrowCastObservationSnapshot snapshot, DateTimeOffset receivedAtUtc) =>
        Mutate(snapshot.Cluster, state =>
        {
            var observer = GetObserver(state, snapshot.Observer, receivedAtUtc);
            observer.Snapshot = snapshot;
            observer.LastSeenAtUtc = receivedAtUtc;
            observer.Connected = true;
        });

    public void Append(
        CrowCastClusterKey cluster,
        CrowCastObserverIdentity observer,
        IReadOnlyCollection<CrowCastObservationEvent> events,
        DateTimeOffset receivedAtUtc) =>
        Mutate(cluster, state =>
        {
            var observerState = GetObserver(state, observer, receivedAtUtc);
            observerState.LastSeenAtUtc = receivedAtUtc;
            foreach (var item in events.OrderBy(item => item.Sequence))
            {
                var eventKey = $"{observer}:{item.Sequence}";
                if (!state.EventKeys.Add(eventKey)) continue;
                state.Events.Enqueue((observer, item));
                while (state.Events.Count > _options.MaximumEventsPerCluster)
                {
                    var removed = state.Events.Dequeue();
                    state.EventKeys.Remove($"{removed.Observer}:{removed.Event.Sequence}");
                }
            }
        });

    public void Touch(CrowCastClusterKey cluster, CrowCastObserverIdentity observer, DateTimeOffset receivedAtUtc) =>
        Mutate(cluster, state => GetObserver(state, observer, receivedAtUtc).LastSeenAtUtc = receivedAtUtc);

    public void Disconnect(CrowCastClusterKey cluster, CrowCastObserverIdentity observer, DateTimeOffset receivedAtUtc) =>
        Mutate(cluster, state =>
        {
            var value = GetObserver(state, observer, receivedAtUtc);
            value.Connected = false;
            value.LastSeenAtUtc = receivedAtUtc;
        });

    public IReadOnlyList<CrowCastClusterView> GetClusters(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _clusters.Values.Select(state => CreateView(state, now))
                .OrderBy(view => view.Key.Environment, StringComparer.OrdinalIgnoreCase)
                .ThenBy(view => view.Key.Service, StringComparer.OrdinalIgnoreCase)
                .ThenBy(view => view.Key.Cluster, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public CrowCastClusterView? GetCluster(CrowCastClusterKey key, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _clusters.TryGetValue(Key(key), out var state) ? CreateView(state, now) : null;
        }
    }

    public IReadOnlyList<CrowCastObservationEvent> GetEvents(CrowCastClusterKey key, long afterSequence = 0, int take = 500)
    {
        lock (_gate)
        {
            return _clusters.TryGetValue(Key(key), out var state)
                ? state.Events.Select(item => item.Event).Where(item => item.Sequence > afterSequence).OrderByDescending(item => item.OccurredAtUtc)
                    .Take(Math.Clamp(take, 1, 2_000)).ToArray()
                : [];
        }
    }

    private void Mutate(CrowCastClusterKey cluster, Action<ClusterState> mutation)
    {
        lock (_gate)
        {
            var key = Key(cluster);
            if (!_clusters.TryGetValue(key, out var state))
            {
                state = new ClusterState(cluster);
                _clusters.Add(key, state);
            }

            mutation(state);
        }

        Changed?.Invoke(cluster);
    }

    private ObserverState GetObserver(ClusterState state, CrowCastObserverIdentity observer, DateTimeOffset now)
    {
        var key = observer.ToString();
        if (state.Observers.TryGetValue(key, out var value)) return value;
        if (state.Observers.Count >= _options.MaximumObserversPerCluster)
        {
            var oldest = state.Observers.MinBy(pair => pair.Value.LastSeenAtUtc);
            if (!string.IsNullOrEmpty(oldest.Key)) state.Observers.Remove(oldest.Key);
        }

        value = new ObserverState(observer, now);
        state.Observers.Add(key, value);
        return value;
    }

    private CrowCastClusterView CreateView(ClusterState state, DateTimeOffset now)
    {
        var observers = state.Observers.Values.Select(observer => new CrowCastObserverView(
            observer.Identity,
            observer.Snapshot,
            observer.LastSeenAtUtc,
            observer.Connected,
            Freshness(observer, now))).OrderBy(view => view.Identity.NodeId, StringComparer.Ordinal).ToArray();
        var fresh = observers.Where(observer => observer.Freshness == "Fresh" && observer.Snapshot is not null).ToArray();
        var representative = fresh.OrderByDescending(observer => observer.LastSeenAtUtc).FirstOrDefault()?.Snapshot;
        var coordinators = fresh.Select(observer => observer.Snapshot!.Coordinator?.ToString() ?? string.Empty)
            .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).Count();
        var signatures = fresh.Select(observer => string.Join('|', observer.Snapshot!.Members
            .OrderBy(member => member.Identity.ToString(), StringComparer.Ordinal)
            .Select(member => $"{member.Identity}:{member.Status}"))).Distinct(StringComparer.Ordinal).Count();
        var disagreement = signatures > 1;
        var health = fresh.Length == 0 ? "Offline"
            : fresh.Length < observers.Length ? "Degraded"
            : coordinators > 1 ? "CoordinatorDisagreement"
            : disagreement ? "PartitionSuspected"
            : representative?.Members.Any(member => member.Status == "Suspect") == true ? "Degraded"
            : "Healthy";
        return new(
            state.Key,
            health,
            observers.Length,
            fresh.Length,
            representative?.Members.Count(member => member.Status == "Alive") ?? 0,
            representative?.Members.Count(member => member.Status == "Suspect") ?? 0,
            representative?.Members.Count(member => member.Status == "Dead") ?? 0,
            representative?.Members.Count(member => member.Status == "Left") ?? 0,
            coordinators,
            disagreement,
            observers.Max(observer => observer.LastSeenAtUtc),
            observers);
    }

    private string Freshness(ObserverState observer, DateTimeOffset now)
    {
        var age = now - observer.LastSeenAtUtc;
        return age >= _options.ObserverOfflineAfter ? "Offline"
            : age >= _options.ObserverStaleAfter || !observer.Connected ? "Stale"
            : "Fresh";
    }

    private static string Key(CrowCastClusterKey key) =>
        $"{key.Environment}\u001f{key.Scope}\u001f{key.Service}\u001f{key.Cluster}";

    private sealed class ClusterState(CrowCastClusterKey key)
    {
        public CrowCastClusterKey Key { get; } = key;
        public Dictionary<string, ObserverState> Observers { get; } = new(StringComparer.Ordinal);
        public Queue<(CrowCastObserverIdentity Observer, CrowCastObservationEvent Event)> Events { get; } = new();
        public HashSet<string> EventKeys { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ObserverState(CrowCastObserverIdentity identity, DateTimeOffset lastSeenAtUtc)
    {
        public CrowCastObserverIdentity Identity { get; } = identity;
        public CrowCastObservationSnapshot? Snapshot { get; set; }
        public DateTimeOffset LastSeenAtUtc { get; set; } = lastSeenAtUtc;
        public bool Connected { get; set; }
    }
}
