using Microsoft.AspNetCore.SignalR;

namespace CrowCast.WebUI.Realtime;

public sealed class CrowCastWebUIHub : Hub
{
    public Task Watch() => Groups.AddToGroupAsync(Context.ConnectionId, "crowcast:local");
}
