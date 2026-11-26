using System.Net;
using CrowCast.Backends;
using CrowCast.ControlPlane.Client.DependencyInjection;
using CrowCast.ControlPlane.DependencyInjection;
using CrowCast.ControlPlane.Storage;
using CrowCast.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrowCast.Tests.ControlPlane;

public sealed class ControlPlaneGrpcIntegrationTests
{
    [Fact]
    public async Task Reporter_streams_local_observations_to_control_plane()
    {
        var serverBuilder = WebApplication.CreateBuilder();
        serverBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = HttpProtocols.Http2));
        serverBuilder.Services.AddCrowCastControlPlane();
        await using var server = serverBuilder.Build();
        server.MapCrowCastControlPlane();
        await server.StartAsync();
        var endpoint = new Uri(server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        var store = server.Services.GetRequiredService<ICrowCastControlPlaneStore>();

        var clientBuilder = Host.CreateApplicationBuilder();
        clientBuilder.Services.AddSingleton<ICrowCastBackend, FakeCrowCastBackend>();
        clientBuilder.Services.AddCrowCast(options => options.ClusterName = "integration");
        clientBuilder.Services.AddCrowCastControlPlaneClient(options =>
        {
            options.Endpoint = endpoint;
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            options.SnapshotInterval = TimeSpan.FromMilliseconds(100);
            options.MinimumReconnectDelay = TimeSpan.FromMilliseconds(20);
            options.MaximumReconnectDelay = TimeSpan.FromMilliseconds(100);
        });
        using var client = clientBuilder.Build();
        await client.StartAsync();
        await WaitForAsync(() => store.GetClusters(DateTimeOffset.UtcNow) is [{ AliveMembers: 1, Observers: 1 }]);
        var cluster = Assert.Single(store.GetClusters(DateTimeOffset.UtcNow));
        Assert.Equal("integration", cluster.Key.Cluster);
        Assert.Equal(1, cluster.AliveMembers);
        Assert.Equal(1, cluster.Observers);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.StopAsync(stop.Token);
        await server.StopAsync(stop.Token);
    }

    [Fact]
    public void Extension_methods_validate_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => CrowCastControlPlaneEndpointRouteBuilderExtensions.MapCrowCastControlPlane(null!));
        Assert.Throws<ArgumentNullException>(() => CrowCast.ControlPlane.Client.DependencyInjection.CrowCastControlPlaneClientServiceCollectionExtensions.AddCrowCastControlPlaneClient(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddCrowCastControlPlaneClient(null!));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var index = 0; index < 200 && !condition(); index++) await Task.Delay(20);
        Assert.True(condition());
    }
}
