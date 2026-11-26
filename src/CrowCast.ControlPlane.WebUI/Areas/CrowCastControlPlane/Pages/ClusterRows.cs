using CrowCast.ControlPlane.Storage;
using CrowCast.Observability.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace CrowCast.ControlPlane.WebUI.Areas.CrowCastControlPlane.Pages;

internal static class ClusterRows
{
    public static object[] Create(ICrowCastControlPlaneStore store, DateTimeOffset now, string detailPath) => store.GetClusters(now).Select(cluster =>
    {
        return (object)new
        {
            id = cluster.Key.ToString(),
            dashboard = "View dashboard",
            cluster.Key.Environment,
            cluster.Key.Scope,
            cluster.Key.Service,
            cluster = cluster.Key.Cluster,
            cluster.Health,
            cluster.Observers,
            cluster.FreshObservers,
            cluster.AliveMembers,
            cluster.SuspectMembers,
            cluster.DeadMembers,
            cluster.LeftMembers,
            cluster.CoordinatorCount,
            agreement = cluster.MembershipDisagreement ? "Disagreement" : "In agreement",
            cluster.LastSeenAtUtc,
            url = DashboardUrl(detailPath, cluster.Key)
        };
    }).ToArray();

    private static string DashboardUrl(string detailPath, CrowCastClusterKey key) => QueryHelpers.AddQueryString(detailPath, new Dictionary<string, string?>
    {
        ["environment"] = key.Environment,
        ["service"] = key.Service,
        ["cluster"] = key.Cluster,
        ["scope"] = key.Scope
    });
}
