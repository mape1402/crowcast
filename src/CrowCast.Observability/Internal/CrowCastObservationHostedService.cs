using Microsoft.Extensions.Hosting;

namespace CrowCast.Observability.Internal;

internal sealed class CrowCastObservationHostedService(CrowCastObservationSource source) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => source.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => source.StopAsync(cancellationToken);
}
