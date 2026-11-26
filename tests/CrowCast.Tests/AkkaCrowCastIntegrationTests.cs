using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
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
    public async Task Two_replicas_support_broadcast_member_and_coordinator_delivery_then_leave()
    {
        var seedPort = GetFreePort();
        var replicaPort = GetFreePort();
        await using var seed = await ClusterRuntime.StartAsync(seedPort, seedPort);
        await using var replica = await ClusterRuntime.StartAsync(replicaPort, seedPort);

        await WaitUntilAsync(
            () => seed.CrowCast.Members.Count(member => member.Status == CrowCastMemberStatus.Alive) == 2 &&
                  replica.CrowCast.Members.Count(member => member.Status == CrowCastMemberStatus.Alive) == 2 &&
                  seed.CrowCast.Coordinator is not null &&
                  seed.CrowCast.Coordinator == replica.CrowCast.Coordinator,
            TimeSpan.FromSeconds(20));
        await Task.Delay(TimeSpan.FromSeconds(1));

        var broadcast = CrowCastMessage.Create("integration.broadcast", Encoding.UTF8.GetBytes("all"));
        await replica.CrowCast.PublishToAllAsync(broadcast);

        await WaitUntilAsync(
            () => seed.Events.Events.OfType<CrowCastMessageReceived>()
                      .Any(received => received.Message.Id == broadcast.Id) &&
                  replica.Events.Events.OfType<CrowCastMessageReceived>()
                      .Any(received => received.Message.Id == broadcast.Id),
            TimeSpan.FromSeconds(10));

        await replica.CrowCast.PublishToAllAsync(new IntegrationMessage("typed-broadcast"));
        await WaitUntilAsync(
            () => seed.TypedMessages.Messages.Any(message => message.Text == "typed-broadcast") &&
                  replica.TypedMessages.Messages.Any(message => message.Text == "typed-broadcast"),
            TimeSpan.FromSeconds(10));

        var direct = CrowCastMessage.Create("integration.member", Encoding.UTF8.GetBytes("seed"));
        await replica.CrowCast.PublishToMemberAsync(direct, seed.CrowCast.LocalNode);
        await WaitUntilAsync(
            () => seed.Events.Events.OfType<CrowCastMessageReceived>()
                .Any(received => received.Message.Id == direct.Id),
            TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(
            replica.Events.Events.OfType<CrowCastMessageReceived>(),
            received => received.Message.Id == direct.Id);

        var coordinatorMessage = CrowCastMessage.Create(
            "integration.coordinator",
            Encoding.UTF8.GetBytes("coordinator"));
        var coordinator = Assert.IsType<CrowCastNodeIdentity>(replica.CrowCast.Coordinator);
        var coordinatorRuntime = coordinator == seed.CrowCast.LocalNode ? seed : replica;
        var otherRuntime = coordinator == seed.CrowCast.LocalNode ? replica : seed;
        await replica.CrowCast.PublishToCoordinatorAsync(coordinatorMessage);
        await WaitUntilAsync(
            () => coordinatorRuntime.Events.Events.OfType<CrowCastMessageReceived>()
                .Any(received => received.Message.Id == coordinatorMessage.Id),
            TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(
            otherRuntime.Events.Events.OfType<CrowCastMessageReceived>(),
            received => received.Message.Id == coordinatorMessage.Id);

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
            RecordingEventHandler events,
            IntegrationMessageRecorder typedMessages)
        {
            _provider = provider;
            _hostedService = hostedService;
            CrowCast = crowCast;
            Events = events;
            TypedMessages = typedMessages;
        }

        public ICrowCast CrowCast { get; }

        public RecordingEventHandler Events { get; }

        public IntegrationMessageRecorder TypedMessages { get; }

        public static async Task<ClusterRuntime> StartAsync(int port, int seedPort)
        {
            var services = new ServiceCollection();
            var events = new RecordingEventHandler();
            services.AddLogging();
            services.AddSingleton<IntegrationMessageRecorder>();
            services.AddSingleton<ICrowCastEventHandler<CrowCastMemberChanged>>(events);
            services.AddSingleton<ICrowCastEventHandler<CrowCastMessageReceived>>(events);
            services.AddSingleton<ICrowCastEventHandler<CrowCastCoordinatorChanged>>(events);

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
            builder.AddMessageHandler<IntegrationMessage, IntegrationMessageHandler>(
                "integration.message.v1");

            var provider = services.BuildServiceProvider();
            var hostedService = provider.GetServices<IHostedService>().Single();
            var crowCast = provider.GetRequiredService<ICrowCast>();
            await hostedService.StartAsync(CancellationToken.None);
            return new ClusterRuntime(
                provider,
                hostedService,
                crowCast,
                events,
                provider.GetRequiredService<IntegrationMessageRecorder>());
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

    private sealed record IntegrationMessage(string Text);

    private sealed class IntegrationMessageRecorder
    {
        public ConcurrentQueue<IntegrationMessage> Messages { get; } = new();
    }

    private sealed class IntegrationMessageHandler(IntegrationMessageRecorder recorder) :
        ICrowCastMessageHandler<IntegrationMessage>
    {
        public ValueTask HandleAsync(
            CrowCastMessageContext<IntegrationMessage> context,
            CancellationToken cancellationToken)
        {
            recorder.Messages.Enqueue(context.Message);
            return ValueTask.CompletedTask;
        }
    }
}
