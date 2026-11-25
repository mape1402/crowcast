using System.Net;
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
    public async Task Dns_discovery_resolves_distinct_addresses_and_forwards_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        string? resolvedHost = null;
        CancellationToken resolvedToken = default;
        var discovery = new DnsCrowCastNodeDiscovery(
            " service.local ",
            4053,
            (hostName, cancellationToken) =>
            {
                resolvedHost = hostName;
                resolvedToken = cancellationToken;
                IReadOnlyCollection<IPAddress> addresses =
                    [IPAddress.Loopback, IPAddress.Loopback, IPAddress.IPv6Loopback];
                return ValueTask.FromResult(addresses);
            });

        var result = await discovery.DiscoverAsync(cancellation.Token);

        Assert.Equal("service.local", resolvedHost);
        Assert.Equal(cancellation.Token, resolvedToken);
        Assert.Equal(2, result.Count);
        Assert.Contains(new CrowCastEndpoint("127.0.0.1", 4053), result);
        Assert.Contains(new CrowCastEndpoint("::1", 4053), result);
    }

    [Fact]
    public async Task Dns_discovery_validates_configuration_and_resolves_real_dns()
    {
        Assert.Throws<ArgumentException>(() => new DnsCrowCastNodeDiscovery(" ", 4053));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DnsCrowCastNodeDiscovery("localhost", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DnsCrowCastNodeDiscovery("localhost", 65_536));
        Assert.Throws<ArgumentNullException>(() => new DnsCrowCastNodeDiscovery(
            "localhost",
            4053,
            null!));

        var result = await new DnsCrowCastNodeDiscovery("localhost", 4053)
            .DiscoverAsync(CancellationToken.None);

        Assert.NotEmpty(result);
    }

    [Fact]
    public void Dns_discovery_registration_replaces_the_default_discovery()
    {
        var services = new ServiceCollection();
        var builder = services.AddCrowCast();

        builder.UseDnsNodeDiscovery("localhost", 4053);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DnsCrowCastNodeDiscovery>(provider.GetRequiredService<ICrowCastNodeDiscovery>());
        Assert.Throws<ArgumentNullException>(() =>
            CrowCastNodeDiscoveryBuilderExtensions.UseDnsNodeDiscovery(null!, "localhost", 4053));
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
