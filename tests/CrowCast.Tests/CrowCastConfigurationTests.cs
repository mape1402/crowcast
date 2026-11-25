using CrowCast.Backends;
using CrowCast.Akka.DependencyInjection;
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

    [Fact]
    public void Invalid_protection_limits_are_rejected()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICrowCastBackend>(new FakeCrowCastBackend());
        services.AddCrowCast(options =>
        {
            options.MaximumMessageContentBytes = 0;
            options.MaximumReceivedMessagesPerInterval = 0;
        });
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<ICrowCast>());

        Assert.Contains(nameof(CrowCast.Configuration.CrowCastOptions.MaximumMessageContentBytes), exception.Message);
        Assert.Contains(nameof(CrowCast.Configuration.CrowCastOptions.MaximumReceivedMessagesPerInterval), exception.Message);
    }

    [Fact]
    public async Task Akka_frame_must_fit_the_configured_protocol_limits()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddCrowCast();
        builder.UseAkka(options => options.MaximumFrameSizeBytes = 32_000);
        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Single();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => hostedService.StartAsync(CancellationToken.None));

        Assert.Contains(nameof(CrowCast.Akka.Configuration.AkkaCrowCastOptions.MaximumFrameSizeBytes), exception.Message);
    }
}
