using System.Threading.Channels;
using CrowCast.Observability;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace CrowCast.WebUI.Realtime;

internal sealed class CrowCastWebUIChangeDispatcher : BackgroundService
{
    private readonly ICrowCastObservationSource _source;
    private readonly IHubContext<CrowCastWebUIHub> _hub;
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropWrite
    });

    public CrowCastWebUIChangeDispatcher(ICrowCastObservationSource source, IHubContext<CrowCastWebUIHub> hub)
    {
        _source = source;
        _hub = hub;
        _source.Changed += OnChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var _ in _changes.Reader.ReadAllAsync(stoppingToken))
            {
                await _hub.Clients.Group("crowcast:local").SendAsync("crowcast.changed", cancellationToken: stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
    }

    public override void Dispose()
    {
        _source.Changed -= OnChanged;
        base.Dispose();
    }

    private void OnChanged() => _changes.Writer.TryWrite(true);
}
