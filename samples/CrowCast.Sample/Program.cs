using CrowCast.Akka.DependencyInjection;
using CrowCast.Backends;
using CrowCast.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var hostName = ReadArgument("--host") ?? "127.0.0.1";
var port = ReadIntArgument("--port", 4053);
var seedHost = ReadArgument("--seed-host") ?? hostName;
var seedService = ReadArgument("--seed-service");
var seedPort = ReadIntArgument("--seed-port", 4053);
var clusterName = ReadArgument("--cluster") ?? "crowcast-sample";

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Information);

var crowCast = builder.Services.AddCrowCast(options =>
{
    options.ClusterName = clusterName;
    options.ExchangeInterval = TimeSpan.FromSeconds(1);
    options.SuspectAfter = TimeSpan.FromSeconds(8);
    options.DeadAfter = TimeSpan.FromSeconds(20);
    options.DeduplicationWindow = TimeSpan.FromMinutes(2);
    options.MessageTimeToLive = TimeSpan.FromMinutes(2);
    options.Metadata["service"] = "crowcast-sample";
});
crowCast.UseAkka(options =>
{
    options.HostName = hostName;
    options.Port = port;
});
if (string.IsNullOrWhiteSpace(seedService))
{
    crowCast.UseStaticNodeDiscovery(new CrowCastEndpoint(seedHost, seedPort));
}
else
{
    crowCast.UseDnsNodeDiscovery(seedService, seedPort);
}
crowCast.AddEventHandler<SampleEventHandler>();
builder.Services.AddHostedService<SamplePublisher>();

await builder.Build().RunAsync();

string? ReadArgument(string name)
{
    var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

int ReadIntArgument(string name, int defaultValue) =>
    int.TryParse(ReadArgument(name), out var value) ? value : defaultValue;
