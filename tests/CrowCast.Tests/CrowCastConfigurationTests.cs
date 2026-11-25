using CrowCast.Backends;
using CrowCast.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests;

public sealed class CrowCastConfigurationTests
{
    [Fact]
    public async Task Static_discovery_returns_distinct_endpoints()
    {
        var endpoint = new CrowCastEndpoint("127.0.0.1", 4053);
        var discovery = new StaticCrowCastNodeDiscovery([endpoint, endpoint]);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(endpoint, Assert.Single(result));
    }

    [Fact]
    public void Invalid_failure_detection_order_is_rejected()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICrowCastBackend>(new FakeCrowCastBackend());
        services.AddCrowCast(options =>
        {
            options.ExchangeInterval = TimeSpan.FromSeconds(5);
            options.SuspectAfter = TimeSpan.FromSeconds(4);
        });
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ICrowCast>());
    }
}
