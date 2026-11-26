using CrowCast.ControlPlane.Models;
using CrowCast.Observability.Models;

namespace CrowCast.ControlPlane.Storage;

public interface ICrowCastControlPlaneStore
{
    event Action<CrowCastClusterKey>? Changed;

    void Connect(CrowCastClusterKey cluster, CrowCastObserverIdentity observer, DateTimeOffset observedAtUtc);

    void Upsert(CrowCastObservationSnapshot snapshot, DateTimeOffset receivedAtUtc);

    void Append(CrowCastClusterKey cluster, CrowCastObserverIdentity observer, IReadOnlyCollection<CrowCastObservationEvent> events, DateTimeOffset receivedAtUtc);

    void Touch(CrowCastClusterKey cluster, CrowCastObserverIdentity observer, DateTimeOffset receivedAtUtc);

    void Disconnect(CrowCastClusterKey cluster, CrowCastObserverIdentity observer, DateTimeOffset receivedAtUtc);

    IReadOnlyList<CrowCastClusterView> GetClusters(DateTimeOffset now);

    CrowCastClusterView? GetCluster(CrowCastClusterKey key, DateTimeOffset now);

    IReadOnlyList<CrowCastObservationEvent> GetEvents(CrowCastClusterKey key, long afterSequence = 0, int take = 500);
}
