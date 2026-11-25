using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;
using CrowCast.Akka.Configuration;
using CrowCast.Akka.Internal;
using CrowCast.Akka.Transport;
using CrowCast.Backends;
using CrowCast.Membership;
using CrowCast.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests;

public sealed class AkkaBackendCoverageTests
{
    [Fact]
    public async Task Backend_lifecycle_validates_calls_bootstraps_and_disposes_a_running_system()
    {
        var backend = CreateBackend(new StaticCrowCastNodeDiscovery([]));
        var context = CreateContext();

        await backend.StopAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await backend.StartAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await backend.PublishAsync(null!, CancellationToken.None));
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await backend.PublishAsync(CrowCastMessage.Create("cancelled", ReadOnlyMemory<byte>.Empty), cancellation.Token));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await backend.PublishAsync(CrowCastMessage.Create("not-started", ReadOnlyMemory<byte>.Empty), CancellationToken.None));

        await backend.StartAsync(context, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await backend.StartAsync(context, CancellationToken.None));
        await backend.PublishAsync(CrowCastMessage.Create("started", ReadOnlyMemory<byte>.Empty), CancellationToken.None);

        await backend.DisposeAsync();
        await backend.DisposeAsync();
    }

    [Fact]
    public async Task Start_failure_terminates_the_partial_actor_system_and_can_be_stopped_safely()
    {
        var backend = CreateBackend(new ThrowingDiscovery());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await backend.StartAsync(CreateContext(), CancellationToken.None));

        Assert.Equal("discovery failed", exception.Message);
        await backend.StopAsync(CancellationToken.None);
        await backend.DisposeAsync();
    }

    [Fact]
    public async Task Stop_timeout_forces_actor_system_termination()
    {
        var backend = CreateBackend(
            new StaticCrowCastNodeDiscovery([]),
            new AkkaCrowCastOptions
            {
                Port = 0,
                StartupTimeout = TimeSpan.FromSeconds(5),
                ShutdownTimeout = TimeSpan.FromTicks(1)
            });
        await backend.StartAsync(CreateContext(), CancellationToken.None);

        await backend.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stop_terminates_when_the_cluster_actor_is_unavailable()
    {
        var backend = CreateBackend(
            new StaticCrowCastNodeDiscovery([]),
            new AkkaCrowCastOptions
            {
                Port = 0,
                StartupTimeout = TimeSpan.FromSeconds(5),
                ShutdownTimeout = TimeSpan.FromMilliseconds(200)
            });
        await backend.StartAsync(CreateContext(), CancellationToken.None);
        SetPrivateField(backend, "_clusterActor", null);
        SetPrivateField(backend, "_cluster", null);

        await backend.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stop_uses_a_fallback_propagation_delay_when_context_is_unavailable()
    {
        var backend = CreateBackend(new StaticCrowCastNodeDiscovery([]));
        await backend.StartAsync(CreateContext(), CancellationToken.None);
        SetPrivateField(backend, "_context", null);

        await backend.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cluster_actor_rejects_invalid_wire_input_ignores_stale_announcements_and_maps_state_statuses()
    {
        var notifications = new ConcurrentQueue<CrowCastBackendNotification>();
        var backend = CreateBackend(new StaticCrowCastNodeDiscovery([]));
        await backend.StartAsync(CreateContext(notifications), CancellationToken.None);
        var actor = GetPrivateField<IActorRef>(backend, "_clusterActor");
        var now = DateTimeOffset.UtcNow;

        actor.Tell(new CrowCastNodeAnnouncement(
            Guid.Empty,
            Guid.NewGuid(),
            "invalid",
            now,
            new Dictionary<string, string>()));
        actor.Tell(new CrowCastNodeLeaving(
            Guid.Empty,
            Guid.NewGuid(),
            "invalid",
            now,
            new Dictionary<string, string>()));
        actor.Tell(new CrowCastMessageEnvelope(
            Guid.Empty,
            "event",
            [],
            now,
            new Dictionary<string, string>(),
            null,
            null));

        const string endpoint = "akka.tcp://coverage@127.0.0.1:5001";
        actor.Tell(new CrowCastNodeAnnouncement(
            Guid.NewGuid(),
            Guid.NewGuid(),
            endpoint,
            now,
            new Dictionary<string, string>()));
        actor.Tell(new CrowCastNodeAnnouncement(
            Guid.NewGuid(),
            Guid.NewGuid(),
            endpoint,
            now - TimeSpan.FromSeconds(1),
            new Dictionary<string, string>()));

        actor.Tell(CreateClusterState());
        await WaitUntilAsync(
            () => notifications.Any(notification => notification is CrowCastBackendMemberChanged),
            TimeSpan.FromSeconds(5));

        Assert.Contains(notifications, notification => notification is CrowCastBackendMemberChanged);
        await backend.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cluster_actor_replaces_an_older_generation_at_the_same_endpoint()
    {
        var notifications = new ConcurrentQueue<CrowCastBackendNotification>();
        var backend = CreateBackend(new StaticCrowCastNodeDiscovery([]));
        await backend.StartAsync(CreateContext(notifications), CancellationToken.None);
        var actor = GetPrivateField<IActorRef>(backend, "_clusterActor");
        var now = DateTimeOffset.UtcNow;
        const string endpoint = "akka.tcp://coverage@127.0.0.1:5201";
        var firstIdentity = CrowCastNodeIdentity.New();
        var secondIdentity = CrowCastNodeIdentity.New();

        actor.Tell(CreateAnnouncement(firstIdentity, endpoint, now));
        actor.Tell(CreateAnnouncement(secondIdentity, endpoint, now.AddSeconds(1)));

        await WaitUntilAsync(
            () => notifications.OfType<CrowCastBackendMemberChanged>().Any(change =>
                      change.Member.Identity == firstIdentity &&
                      change.Member.Status == CrowCastMemberStatus.Dead) &&
                  notifications.OfType<CrowCastBackendMemberChanged>().Any(change =>
                      change.Member.Identity == secondIdentity &&
                      change.Member.Status == CrowCastMemberStatus.Alive),
            TimeSpan.FromSeconds(5));

        actor.Tell(CreateLeaving(firstIdentity, endpoint, now.AddSeconds(1)));
        await WaitUntilAsync(
            () => notifications.OfType<CrowCastBackendMemberChanged>().Any(change =>
                change.Member.Identity == firstIdentity &&
                change.Member.Status == CrowCastMemberStatus.Left),
            TimeSpan.FromSeconds(5));

        await backend.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cluster_actor_compacts_removed_members_and_preserves_terminal_status()
    {
        var notifications = new ConcurrentQueue<CrowCastBackendNotification>();
        var backend = CreateBackend(new StaticCrowCastNodeDiscovery([]));
        await backend.StartAsync(CreateContext(notifications), CancellationToken.None);
        var actor = GetPrivateField<IActorRef>(backend, "_clusterActor");
        var identity = CrowCastNodeIdentity.New();
        var now = DateTimeOffset.UtcNow;
        const string endpoint = "akka.tcp://coverage@127.0.0.1:5202";

        actor.Tell(CreateAnnouncement(identity, endpoint, now));
        await WaitUntilAsync(
            () => notifications.OfType<CrowCastBackendMemberChanged>().Any(change =>
                change.Member.Identity == identity &&
                change.Member.Status == CrowCastMemberStatus.Alive),
            TimeSpan.FromSeconds(5));

        var memberAddress = Address.Parse(endpoint);
        var downedMember = CreateMember(memberAddress, 20, MemberStatus.Down);
        var upMember = CreateMember(memberAddress, 20, MemberStatus.Up);
        var removedMember = CreateMember(memberAddress, 20, MemberStatus.Removed);
        actor.Tell(new global::Akka.Cluster.ClusterEvent.MemberDowned(downedMember));
        actor.Tell(new global::Akka.Cluster.ClusterEvent.MemberUp(upMember));
        actor.Tell(new global::Akka.Cluster.ClusterEvent.MemberRemoved(removedMember, MemberStatus.Up));
        var unknown = CreateMember(
            Address.Parse("akka.tcp://coverage@127.0.0.1:5299"),
            21,
            MemberStatus.Removed);
        actor.Tell(new global::Akka.Cluster.ClusterEvent.MemberRemoved(unknown, MemberStatus.Up));
        actor.Tell(CreateAnnouncement(identity, endpoint, now));

        await WaitUntilAsync(
            () => notifications.OfType<CrowCastBackendMemberChanged>()
                .Where(change => change.Member.Identity == identity)
                .LastOrDefault()?.Member.Status == CrowCastMemberStatus.Dead,
            TimeSpan.FromSeconds(5));

        await backend.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cluster_actor_bounds_tombstones_and_accepts_an_evicted_identity()
    {
        var notifications = new ConcurrentQueue<CrowCastBackendNotification>();
        var backend = CreateBackend(new StaticCrowCastNodeDiscovery([]));
        await backend.StartAsync(CreateContext(notifications, maximumRetainedTerminalMembers: 1), CancellationToken.None);
        var actor = GetPrivateField<IActorRef>(backend, "_clusterActor");
        var firstIdentity = CrowCastNodeIdentity.New();
        var secondIdentity = CrowCastNodeIdentity.New();
        var now = DateTimeOffset.UtcNow;
        const string firstEndpoint = "akka.tcp://coverage@127.0.0.1:5203";

        actor.Tell(CreateLeaving(firstIdentity, firstEndpoint, now));
        actor.Tell(CreateAnnouncement(firstIdentity, firstEndpoint, now));

        await WaitUntilAsync(
            () => notifications.OfType<CrowCastBackendMemberChanged>().Any(change =>
                change.Member.Identity == firstIdentity &&
                change.Member.Status == CrowCastMemberStatus.Left),
            TimeSpan.FromSeconds(5));

        actor.Tell(CreateLeaving(
            secondIdentity,
            "akka.tcp://coverage@127.0.0.1:5204",
            now.AddSeconds(1)));
        actor.Tell(CreateAnnouncement(firstIdentity, firstEndpoint, now.AddSeconds(2)));

        await WaitUntilAsync(
            () => notifications.OfType<CrowCastBackendMemberChanged>()
                .Where(change => change.Member.Identity == firstIdentity)
                .LastOrDefault()?.Member.Status == CrowCastMemberStatus.Alive,
            TimeSpan.FromSeconds(5));

        await backend.StopAsync(CancellationToken.None);
    }

    private static CrowCastNodeAnnouncement CreateAnnouncement(
        CrowCastNodeIdentity identity,
        string endpoint,
        DateTimeOffset startedAtUtc) =>
        new(
            identity.NodeId.Value,
            identity.Generation.Value,
            endpoint,
            startedAtUtc,
            new Dictionary<string, string>());

    private static CrowCastNodeLeaving CreateLeaving(
        CrowCastNodeIdentity identity,
        string endpoint,
        DateTimeOffset leftAtUtc) =>
        new(
            identity.NodeId.Value,
            identity.Generation.Value,
            endpoint,
            leftAtUtc,
            new Dictionary<string, string>());

    private static AkkaCrowCastBackend CreateBackend(
        ICrowCastNodeDiscovery discovery,
        AkkaCrowCastOptions? options = null) =>
        new(
            discovery,
            Options.Create(options ?? new AkkaCrowCastOptions
            {
                Port = 0,
                StartupTimeout = TimeSpan.FromSeconds(5),
                ShutdownTimeout = TimeSpan.FromSeconds(2)
            }),
            NullLoggerFactory.Instance,
            NullLogger<AkkaCrowCastBackend>.Instance,
            TimeProvider.System);

    private static CrowCastBackendContext CreateContext(
        ConcurrentQueue<CrowCastBackendNotification>? notifications = null,
        int maximumRetainedTerminalMembers = 1_024)
    {
        var limits = new CrowCastProtocolLimits(
            64 * 1024,
            128,
            32,
            64,
            512,
            2048,
            TimeSpan.FromSeconds(30));
        var settings = new CrowCastBackendSettings(
            $"coverage-{Guid.NewGuid():N}",
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            new Dictionary<string, string>(),
            limits,
            maximumRetainedTerminalMembers);
        return new CrowCastBackendContext(
            CrowCast.Membership.CrowCastNodeIdentity.New(),
            settings,
            (notification, _) =>
            {
                notifications?.Enqueue(notification);
                return ValueTask.CompletedTask;
            });
    }

    private static global::Akka.Cluster.ClusterEvent.CurrentClusterState CreateClusterState()
    {
        var statuses = new[]
        {
            MemberStatus.Up,
            MemberStatus.Leaving,
            MemberStatus.Down,
            MemberStatus.Joining
        };
        var members = statuses.Select((status, index) =>
        {
            var address = Address.Parse($"akka.tcp://coverage@127.0.0.1:{5100 + index}");
            return CreateMember(address, index + 1, status);
        }).ToImmutableSortedSet();
        var unreachable = ImmutableHashSet.Create(members.Last());

        return new global::Akka.Cluster.ClusterEvent.CurrentClusterState(
            members,
            unreachable,
            ImmutableHashSet<Address>.Empty,
            members.First().Address,
            ImmutableDictionary<string, Address>.Empty);
    }

    private static global::Akka.Cluster.Member CreateMember(Address address, int uid, MemberStatus status)
    {
        var method = typeof(global::Akka.Cluster.Member).GetMethod(
            "Create",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(UniqueAddress),
                typeof(int),
                typeof(MemberStatus),
                typeof(ImmutableHashSet<string>),
                typeof(AppVersion)
            ],
            modifiers: null) ?? throw new InvalidOperationException("Akka member factory was not found.");
        return (global::Akka.Cluster.Member)(method.Invoke(
            null,
            [new UniqueAddress(address, uid), uid, status, ImmutableHashSet<string>.Empty, AppVersion.Zero])
            ?? throw new InvalidOperationException("Akka member factory returned null."));
    }

    private static T GetPrivateField<T>(object instance, string name) where T : class =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Field {name} was null."));

    private static void SetPrivateField(object instance, string name, object? value) =>
        (instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {name} was not found."))
        .SetValue(instance, value);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected Akka notification was not observed.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class ThrowingDiscovery : ICrowCastNodeDiscovery
    {
        public ValueTask<IReadOnlyCollection<CrowCastEndpoint>> DiscoverAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<IReadOnlyCollection<CrowCastEndpoint>>(
                new InvalidOperationException("discovery failed"));
    }
}
