using CrowCast.Akka.DependencyInjection;
using CrowCast.Backends;
using CrowCast.ControlPlane.Client.DependencyInjection;
using CrowCast.DependencyInjection;
using CrowCast.Observability.DependencyInjection;
using CrowCast.WebUI;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

var gossipHost = Read("--gossip-host") ?? "127.0.0.1";
var gossipPort = ReadInt("--gossip-port", 4053);
var seedHost = Read("--seed-host") ?? gossipHost;
var seedPort = ReadInt("--seed-port", 4053);
var clusterName = Read("--cluster") ?? "crowcast-dashboard-sample";
var serviceName = Read("--service") ?? "crowcast-webui-sample";
var controlPlane = Read("--control-plane");

var crowCast = builder.Services.AddCrowCast(options =>
{
    options.ClusterName = clusterName;
    options.ExchangeInterval = TimeSpan.FromSeconds(1);
    options.SuspectAfter = TimeSpan.FromSeconds(8);
    options.DeadAfter = TimeSpan.FromSeconds(20);
    options.Metadata["service"] = serviceName;
});
crowCast.UseAkka(options =>
{
    options.HostName = gossipHost;
    options.Port = gossipPort;
});
crowCast.UseStaticNodeDiscovery(new CrowCastEndpoint(seedHost, seedPort));

builder.Services.AddCrowCastObservability(options =>
{
    options.ServiceName = serviceName;
    options.IncludedMetadataKeys.Add("service");
});
builder.Services.AddCrowCastWebUI();
if (Uri.TryCreate(controlPlane, UriKind.Absolute, out var controlPlaneEndpoint))
{
    builder.Services.AddCrowCastControlPlaneClient(options => options.Endpoint = controlPlaneEndpoint);
}

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapCrowCastWebUI();
app.MapGet("/", () => Results.Redirect("/crowcast"));
await app.RunAsync();

string? Read(string name)
{
    var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

int ReadInt(string name, int fallback) => int.TryParse(Read(name), out var value) ? value : fallback;
