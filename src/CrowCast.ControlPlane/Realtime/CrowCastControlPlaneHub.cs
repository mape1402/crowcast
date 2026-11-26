using Microsoft.AspNetCore.SignalR;

namespace CrowCast.ControlPlane.Realtime;

public sealed class CrowCastControlPlaneHub : Hub
{
    public Task WatchFleet() => Groups.AddToGroupAsync(Context.ConnectionId, "crowcast:fleet");

    public Task WatchCluster(string clusterKey) => string.IsNullOrWhiteSpace(clusterKey)
        ? Task.CompletedTask
        : Groups.AddToGroupAsync(Context.ConnectionId, $"crowcast:cluster:{clusterKey}");
}
