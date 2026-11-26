using Microsoft.Extensions.Hosting;

namespace CrowCast.ControlPlane.Client.Internal;

internal sealed class CrowCastControlPlaneReporterHostedService(CrowCastControlPlaneReporter reporter) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => reporter.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => reporter.StopAsync(cancellationToken);
}
