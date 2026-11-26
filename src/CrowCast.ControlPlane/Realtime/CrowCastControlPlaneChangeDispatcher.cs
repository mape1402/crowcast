using System.Threading.Channels;
using CrowCast.ControlPlane.Storage;
using CrowCast.Observability.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace CrowCast.ControlPlane.Realtime;

internal sealed class CrowCastControlPlaneChangeDispatcher : BackgroundService
{
    private readonly ICrowCastControlPlaneStore _store;
    private readonly IHubContext<CrowCastControlPlaneHub> _hub;
    private readonly Channel<CrowCastClusterKey> _changes = Channel.CreateBounded<CrowCastClusterKey>(new BoundedChannelOptions(1_024)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    public CrowCastControlPlaneChangeDispatcher(ICrowCastControlPlaneStore store, IHubContext<CrowCastControlPlaneHub> hub)
    {
        _store = store;
        _hub = hub;
        _store.Changed += OnChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var cluster in _changes.Reader.ReadAllAsync(stoppingToken))
            {
                var key = cluster.ToString();
                await _hub.Clients.Groups("crowcast:fleet", $"crowcast:cluster:{key}")
                    .SendAsync("crowcast.changed", new { cluster = key }, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
    }

    public override void Dispose()
    {
        _store.Changed -= OnChanged;
        base.Dispose();
    }

    private void OnChanged(CrowCastClusterKey cluster) => _changes.Writer.TryWrite(cluster);
}
