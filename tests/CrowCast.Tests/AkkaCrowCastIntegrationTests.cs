using System.Net;
using System.Net.Sockets;
using System.Text;
using CrowCast.Akka.DependencyInjection;
using CrowCast.Backends;
using CrowCast.DependencyInjection;
using CrowCast.Events;
using CrowCast.Membership;
using CrowCast.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrowCast.Tests;

public sealed class AkkaCrowCastIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Two_replicas_discover_exchange_messages_and_leave()
    {
        var seedPort = GetFreePort();
        var replicaPort = GetFreePort();
        await using var seed = await ClusterRuntime.StartAsync(seedPort, seedPort);
        await using var replica = await ClusterRuntime.StartAsync(replicaPort, seedPort);

        await WaitUntilAsync(
            () => seed.CrowCast.Members.Count(member => member.Status == CrowCastMemberStatus.Alive) == 2 &&
                  replica.CrowCast.Members.Count(member => member.Status == CrowCastMemberStatus.Alive) == 2,
            TimeSpan.FromSeconds(20));

        var message = CrowCastMessage.Create("integration", Encoding.UTF8.GetBytes("hello"));
        await replica.CrowCast.PublishAsync(message);

        await WaitUntilAsync(
            () => seed.Events.Events.OfType<CrowCastMessageReceived>()
                .Any(received => received.Message.Id == message.Id),
            TimeSpan.FromSeconds(10));

        var replicaIdentity = replica.CrowCast.LocalNode;
        await replica.DisposeAsync();

        await WaitUntilAsync(
            () => seed.CrowCast.Members.Any(member =>
                member.Identity == replicaIdentity && member.Status == CrowCastMemberStatus.Left),
            TimeSpan.FromSeconds(10));

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(
            CrowCastMemberStatus.Left,
            seed.CrowCast.Members.Single(member => member.Identity == replicaIdentity).Status);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The CrowCast cluster did not reach the expected state.");
            }

            await Task.Delay(100);
        }
    }

    private sealed class ClusterRuntime : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IHostedService _hostedService;
        private int _disposed;

        private ClusterRuntime(
            ServiceProvider provider,
            IHostedService hostedService,
            ICrowCast crowCast,
            RecordingEventHandler events)
        {
            _provider = provider;
            _hostedService = hostedService;
            CrowCast = crowCast;
            Events = events;
        }

        public ICrowCast CrowCast { get; }

        public RecordingEventHandler Events { get; }

        public static async Task<ClusterRuntime> StartAsync(int port, int seedPort)
        {
            var services = new ServiceCollection();
            var events = new RecordingEventHandler();
            services.AddLogging();
            services.AddSingleton<ICrowCastEventHandler>(events);

            var builder = services.AddCrowCast(options =>
            {
                options.ClusterName = "crowcast-integration";
                options.ExchangeInterval = TimeSpan.FromMilliseconds(250);
                options.SuspectAfter = TimeSpan.FromSeconds(3);
                options.DeadAfter = TimeSpan.FromSeconds(6);
                options.DeduplicationWindow = TimeSpan.FromSeconds(30);
                options.MessageTimeToLive = TimeSpan.FromSeconds(30);
            });
            builder.UseAkka(options =>
            {
                options.HostName = "127.0.0.1";
                options.Port = port;
                options.StartupTimeout = TimeSpan.FromSeconds(10);
                options.ShutdownTimeout = TimeSpan.FromSeconds(10);
            });
            builder.UseStaticNodeDiscovery(new CrowCastEndpoint("127.0.0.1", seedPort));

            var provider = services.BuildServiceProvider();
            var hostedService = provider.GetServices<IHostedService>().Single();
            var crowCast = provider.GetRequiredService<ICrowCast>();
            await hostedService.StartAsync(CancellationToken.None);
            return new ClusterRuntime(provider, hostedService, crowCast, events);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            await _hostedService.StopAsync(CancellationToken.None);
            await _provider.DisposeAsync();
        }
    }
}
