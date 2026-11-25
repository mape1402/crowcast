using System.Text;
using CrowCast;
using CrowCast.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal sealed class SamplePublisher(ICrowCast crowCast, ILogger<SamplePublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Replica started as random node {NodeId}, generation {Generation}",
            crowCast.LocalNode.NodeId,
            crowCast.LocalNode.Generation);

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            var content = Encoding.UTF8.GetBytes($"hello at {DateTimeOffset.UtcNow:O}");
            await crowCast.PublishAsync(CrowCastMessage.Create("sample.heartbeat", content), stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
