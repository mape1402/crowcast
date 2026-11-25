using System.Text;
using CrowCast.Backends;
using CrowCast.Events;
using CrowCast.Membership;
using CrowCast.Messaging;

namespace CrowCast.Tests;

public sealed class CrowCastServiceTests
{
    [Fact]
    public async Task Each_runtime_gets_a_random_identity_and_generation()
    {
        await using var first = await CrowCastRuntimeHarness.StartAsync();
        await using var second = await CrowCastRuntimeHarness.StartAsync();

        Assert.NotEqual(first.CrowCast.LocalNode.NodeId, second.CrowCast.LocalNode.NodeId);
        Assert.NotEqual(first.CrowCast.LocalNode.Generation, second.CrowCast.LocalNode.Generation);
    }

    [Fact]
    public async Task Terminal_members_are_not_resurrected_and_history_is_bounded()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
            options.MaximumRetainedTerminalMembers = 1);
        var first = CrowCastNodeIdentity.New();
        var second = CrowCastNodeIdentity.New();
        var now = DateTimeOffset.UtcNow;

        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
            new CrowCastMember(first, CrowCastMemberStatus.Alive, now, now, "first")));
        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
            new CrowCastMember(first, CrowCastMemberStatus.Dead, now, now.AddSeconds(1), "first")));
        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
            new CrowCastMember(first, CrowCastMemberStatus.Dead, now, now.AddSeconds(2), "first")));
        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
            new CrowCastMember(first, CrowCastMemberStatus.Alive, now, now.AddSeconds(3), "first")));

        Assert.Equal(
            CrowCastMemberStatus.Dead,
            runtime.CrowCast.Members.Single(member => member.Identity == first).Status);

        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
            new CrowCastMember(second, CrowCastMemberStatus.Left, now, now.AddSeconds(4), "second")));

        Assert.DoesNotContain(runtime.CrowCast.Members, member => member.Identity == first);
        Assert.Equal(
            CrowCastMemberStatus.Left,
            runtime.CrowCast.Members.Single(member => member.Identity == second).Status);
        Assert.Contains(
            runtime.Events.Events.OfType<CrowCastMemberChanged>(),
            change => change.Current.Identity == first && change.Current.Status == CrowCastMemberStatus.Dead);
    }

    [Fact]
    public async Task Publish_assigns_the_local_node_as_contextual_origin()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync();
        var message = CrowCastMessage.Create("work.available", Encoding.UTF8.GetBytes("42"));

        await runtime.CrowCast.PublishAsync(message);

        var published = Assert.Single(runtime.Backend.PublishedMessages);
        Assert.Equal(runtime.CrowCast.LocalNode, published.Origin);
        Assert.Equal(1, runtime.Diagnostics.GetSnapshot().MessagesPublished);
    }

    [Fact]
    public async Task Duplicate_message_ids_are_delivered_once_inside_the_window()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync();
        var message = new CrowCastMessage(
            CrowCastMessageId.New(),
            "presence.changed",
            Encoding.UTF8.GetBytes("online"),
            DateTimeOffset.UtcNow);

        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(message));
        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(message));

        Assert.Single(runtime.Events.Events.OfType<CrowCastMessageReceived>());
        var snapshot = runtime.Diagnostics.GetSnapshot();
        Assert.Equal(1, snapshot.MessagesReceived);
        Assert.Equal(1, snapshot.DuplicateMessages);
    }

    [Fact]
    public async Task Expired_messages_are_not_delivered()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.MessageTimeToLive = TimeSpan.FromSeconds(5);
            options.DeduplicationWindow = TimeSpan.FromSeconds(5);
        });
        var message = new CrowCastMessage(
            CrowCastMessageId.New(),
            "expired",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));

        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(message));

        Assert.Empty(runtime.Events.Events.OfType<CrowCastMessageReceived>());
        Assert.Equal(1, runtime.Diagnostics.GetSnapshot().ExpiredMessages);
    }

    [Fact]
    public async Task Oversized_outbound_messages_are_rejected_before_reaching_the_backend()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.MaximumMessageContentBytes = 4;
        });
        var message = CrowCastMessage.Create("oversized", new byte[5]);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await runtime.CrowCast.PublishAsync(message));

        Assert.Contains("content_too_large", exception.Message);
        Assert.Empty(runtime.Backend.PublishedMessages);
        Assert.Equal(1, runtime.Diagnostics.GetSnapshot().RejectedMessages);
    }

    [Fact]
    public async Task Outbound_temporal_validation_distinguishes_future_and_expired_messages()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.MaximumClockSkew = TimeSpan.FromSeconds(1);
            options.MessageTimeToLive = TimeSpan.FromSeconds(5);
            options.DeduplicationWindow = TimeSpan.FromSeconds(5);
        });
        var now = DateTimeOffset.UtcNow;
        var future = new CrowCastMessage(
            CrowCastMessageId.New(),
            "future",
            ReadOnlyMemory<byte>.Empty,
            now + TimeSpan.FromMinutes(1));
        var expired = new CrowCastMessage(
            CrowCastMessageId.New(),
            "expired",
            ReadOnlyMemory<byte>.Empty,
            now - TimeSpan.FromMinutes(1));

        var futureException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await runtime.CrowCast.PublishAsync(future));
        var expiredException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await runtime.CrowCast.PublishAsync(expired));

        Assert.Contains("timestamp_too_far_in_future", futureException.Message);
        Assert.Contains("expired", expiredException.Message);
        Assert.Equal(2, runtime.Diagnostics.GetSnapshot().RejectedMessages);
    }

    [Fact]
    public async Task Publish_preserves_an_explicit_contextual_origin()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync();
        var origin = CrowCastNodeIdentity.New();
        var message = new CrowCastMessage(
            CrowCastMessageId.New(),
            "explicit-origin",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow,
            origin: origin);

        await runtime.CrowCast.PublishAsync(message);

        Assert.Equal(origin, Assert.Single(runtime.Backend.PublishedMessages).Origin);
    }

    [Fact]
    public async Task Oversized_inbound_messages_are_rejected_without_dispatching_an_event()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.MaximumMessageContentBytes = 4;
        });
        var message = CrowCastMessage.Create("oversized", new byte[5]);

        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(message));

        Assert.Empty(runtime.Events.Events.OfType<CrowCastMessageReceived>());
        Assert.Equal(1, runtime.Diagnostics.GetSnapshot().RejectedMessages);
    }

    [Fact]
    public async Task Messages_too_far_in_the_future_are_rejected()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.MaximumClockSkew = TimeSpan.FromSeconds(1);
        });
        var message = new CrowCastMessage(
            CrowCastMessageId.New(),
            "future",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1));

        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(message));

        Assert.Empty(runtime.Events.Events.OfType<CrowCastMessageReceived>());
        Assert.Equal(1, runtime.Diagnostics.GetSnapshot().RejectedMessages);
    }

    [Fact]
    public async Task Receive_rate_limit_drops_messages_above_the_configured_burst()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.MaximumReceivedMessagesPerInterval = 2;
            options.ReceivedMessageRateLimitInterval = TimeSpan.FromMinutes(1);
        });

        for (var index = 0; index < 3; index++)
        {
            await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(
                CrowCastMessage.Create("burst", BitConverter.GetBytes(index))));
        }

        Assert.Equal(2, runtime.Events.Events.OfType<CrowCastMessageReceived>().Count());
        var snapshot = runtime.Diagnostics.GetSnapshot();
        Assert.Equal(2, snapshot.MessagesReceived);
        Assert.Equal(1, snapshot.RejectedMessages);
        Assert.Equal(1, snapshot.RateLimitedMessages);
    }

    [Fact]
    public async Task Invalid_membership_metadata_is_rejected()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.MaximumMetadataEntries = 0;
        });
        var remote = CrowCastNodeIdentity.New();
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < 100; index++)
        {
            await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
                new CrowCastMember(
                    remote,
                    CrowCastMemberStatus.Alive,
                    now,
                    now,
                    "akka.tcp://remote",
                    new Dictionary<string, string> { ["unexpected"] = "value" })));
        }

        Assert.DoesNotContain(runtime.CrowCast.Members, member => member.Identity == remote);
        Assert.Equal(100, runtime.Diagnostics.GetSnapshot().RejectedMembershipUpdates);
    }

    [Fact]
    public async Task Equivalent_member_updates_are_suppressed_but_metadata_changes_are_emitted()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync();
        var remote = CrowCastNodeIdentity.New();
        var now = DateTimeOffset.UtcNow;
        var original = new CrowCastMember(
            remote,
            CrowCastMemberStatus.Alive,
            now,
            now,
            "akka.tcp://remote",
            new Dictionary<string, string> { ["zone"] = "one" });

        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(original));
        var afterOriginal = runtime.Events.Events.OfType<CrowCastMemberChanged>().Count();
        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(original));
        Assert.Equal(afterOriginal, runtime.Events.Events.OfType<CrowCastMemberChanged>().Count());

        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
            new CrowCastMember(
                remote,
                CrowCastMemberStatus.Alive,
                now,
                now,
                "akka.tcp://remote",
                new Dictionary<string, string> { ["zone"] = "two" })));
        Assert.Equal(afterOriginal + 1, runtime.Events.Events.OfType<CrowCastMemberChanged>().Count());

        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
            new CrowCastMember(
                remote,
                CrowCastMemberStatus.Alive,
                now,
                now,
                "akka.tcp://remote",
                new Dictionary<string, string> { ["region"] = "two" })));
        Assert.Equal(afterOriginal + 2, runtime.Events.Events.OfType<CrowCastMemberChanged>().Count());
    }

    [Fact]
    public async Task Coordinator_election_can_be_disabled()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.EnableCoordinatorElection = false;
        });

        Assert.Null(runtime.CrowCast.Coordinator);
        Assert.Empty(runtime.Events.Events.OfType<CrowCastCoordinatorChanged>());
    }

    [Fact]
    public async Task Coordinator_is_the_lowest_alive_random_identity()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync();
        var remote = CrowCastNodeIdentity.New();
        var now = DateTimeOffset.UtcNow;

        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
            new CrowCastMember(remote, CrowCastMemberStatus.Alive, now, now, "akka.tcp://remote")));

        var expected = new[] { runtime.CrowCast.LocalNode, remote }.OrderBy(identity => identity).First();
        Assert.Equal(expected, runtime.CrowCast.Coordinator);

        await runtime.Backend.EmitAsync(new CrowCastBackendMemberChanged(
            new CrowCastMember(remote, CrowCastMemberStatus.Suspect, now, now, "akka.tcp://remote")));

        Assert.Equal(runtime.CrowCast.LocalNode, runtime.CrowCast.Coordinator);
        Assert.Contains(runtime.CrowCast.Members, member =>
            member.Identity == remote && member.Status == CrowCastMemberStatus.Suspect);
    }
}
