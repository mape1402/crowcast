using System.Diagnostics.Metrics;
using CrowCast.Backends;
using CrowCast.DependencyInjection;
using CrowCast.Events;
using CrowCast.Internal;
using CrowCast.Membership;
using CrowCast.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrowCast.Tests;

public sealed class CrowCastServiceCoverageTests
{
    [Fact]
    public async Task Lifecycle_is_idempotent_recovers_from_start_failure_and_rejects_publish_before_start()
    {
        var backend = new FakeCrowCastBackend { StartException = new InvalidOperationException("start failed") };
        await using var provider = CreateProvider(backend);
        var hostedService = provider.GetServices<IHostedService>().Single();
        var crowCast = provider.GetRequiredService<ICrowCast>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await crowCast.PublishAsync(CrowCastMessage.Create("event", ReadOnlyMemory<byte>.Empty)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await crowCast.PublishToCoordinatorAsync(
                CrowCastMessage.Create("coordinator", ReadOnlyMemory<byte>.Empty)));
        await hostedService.StopAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            hostedService.StartAsync(CancellationToken.None));
        Assert.Empty(crowCast.Members);
        Assert.Null(crowCast.Coordinator);

        backend.StartException = null;
        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StartAsync(CancellationToken.None);
        Assert.Equal(2, backend.StartCount);

        await hostedService.StopAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);
        Assert.Equal(1, backend.StopCount);
    }

    [Fact]
    public async Task Disposing_a_started_service_stops_and_disposes_the_backend_once()
    {
        var backend = new FakeCrowCastBackend();
        var provider = CreateProvider(backend);
        var hostedService = provider.GetServices<IHostedService>().Single();
        var concrete = provider.GetRequiredService<CrowCastService>();
        await hostedService.StartAsync(CancellationToken.None);

        await provider.DisposeAsync();
        await concrete.DisposeAsync();

        Assert.Equal(1, backend.StopCount);
        Assert.Equal(1, backend.DisposeCount);
    }

    [Fact]
    public async Task Backend_can_announce_the_local_member_before_core_startup_finishes()
    {
        var backend = new FakeCrowCastBackend { AnnounceLocalNodeOnStart = true };
        await using var provider = CreateProvider(backend);
        var hostedService = provider.GetServices<IHostedService>().Single();
        var crowCast = provider.GetRequiredService<ICrowCast>();

        await hostedService.StartAsync(CancellationToken.None);

        Assert.Equal(crowCast.LocalNode, Assert.Single(crowCast.Members).Identity);
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Backend_publish_failures_propagate_to_the_caller()
    {
        var backend = new FakeCrowCastBackend { PublishException = new InvalidOperationException("publish failed") };
        await using var provider = CreateProvider(backend);
        var hostedService = provider.GetServices<IHostedService>().Single();
        var crowCast = provider.GetRequiredService<ICrowCast>();
        await hostedService.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await crowCast.PublishAsync(CrowCastMessage.Create("event", ReadOnlyMemory<byte>.Empty)));

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Unsupported_backend_notifications_are_ignored()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync();

        await runtime.Backend.EmitAsync(new UnknownNotification());

        Assert.DoesNotContain(runtime.Events.Events, @event => @event is CrowCastMessageReceived);
    }

    [Fact]
    public async Task Handler_failures_are_isolated_but_requested_cancellation_propagates()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(
            configureServices: services => services.AddSingleton<
                ICrowCastEventHandler<CrowCastMessageReceived>,
                ThrowingMessageHandler>());

        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(
            CrowCastMessage.Create("not-cancelled", ReadOnlyMemory<byte>.Empty)));
        Assert.Single(runtime.Events.Events.OfType<CrowCastMessageReceived>());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await runtime.Backend.EmitAsync(
                new CrowCastBackendMessageReceived(CrowCastMessage.Create("cancelled", ReadOnlyMemory<byte>.Empty)),
                cancellation.Token));
    }

    [Fact]
    public async Task Expired_deduplication_entry_can_be_replaced()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.DeduplicationWindow = TimeSpan.FromMilliseconds(100);
            options.MessageTimeToLive = TimeSpan.FromMilliseconds(100);
        });
        var id = CrowCastMessageId.New();

        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(
            new CrowCastMessage(id, "event", ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow)));
        await Task.Delay(150);
        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(
            new CrowCastMessage(id, "event", ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow)));

        Assert.Equal(2, runtime.Events.Events.OfType<CrowCastMessageReceived>().Count());
    }

    [Fact]
    public async Task Deduplication_cleanup_prunes_expired_and_overflow_entries()
    {
        await using var periodicCleanup = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.MaximumDeduplicationEntries = 1000;
            options.DeduplicationWindow = TimeSpan.FromMilliseconds(100);
            options.MessageTimeToLive = TimeSpan.FromMilliseconds(100);
        });
        await periodicCleanup.Backend.EmitAsync(new CrowCastBackendMessageReceived(
            CrowCastMessage.Create("expired-cleanup", ReadOnlyMemory<byte>.Empty)));
        await Task.Delay(150);
        for (var index = 0; index < 255; index++)
        {
            await periodicCleanup.Backend.EmitAsync(new CrowCastBackendMessageReceived(
                CrowCastMessage.Create("periodic-cleanup", BitConverter.GetBytes(index))));
        }

        Assert.Equal(256, periodicCleanup.Diagnostics.GetSnapshot().MessagesReceived);

        await using var overflowCleanup = await CrowCastRuntimeHarness.StartAsync(options =>
        {
            options.MaximumDeduplicationEntries = 2;
        });
        var first = CrowCastMessage.Create("overflow", new byte[] { 1 });
        await overflowCleanup.Backend.EmitAsync(new CrowCastBackendMessageReceived(first));
        await overflowCleanup.Backend.EmitAsync(new CrowCastBackendMessageReceived(CrowCastMessage.Create("overflow", new byte[] { 2 })));
        await overflowCleanup.Backend.EmitAsync(new CrowCastBackendMessageReceived(CrowCastMessage.Create("overflow", new byte[] { 3 })));
        await overflowCleanup.Backend.EmitAsync(new CrowCastBackendMessageReceived(
            new CrowCastMessage(first.Id, first.Type, first.Content, DateTimeOffset.UtcNow)));

        Assert.Equal(4, overflowCleanup.Diagnostics.GetSnapshot().MessagesReceived);
    }

    [Fact]
    public void Telemetry_observable_gauge_reports_every_membership_status()
    {
        var now = DateTimeOffset.UtcNow;
        var members = Enum.GetValues<CrowCastMemberStatus>()
            .Select(status => new CrowCastMember(CrowCastNodeIdentity.New(), status, now, now))
            .ToArray();
        var observedStatuses = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Elysium.CrowCast")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name != "crowcast.members")
            {
                return;
            }

            foreach (var tag in tags)
            {
                if (tag.Key == "crowcast.member.status" && tag.Value is string status)
                {
                    observedStatuses.Add(status);
                }
            }
        });
        listener.Start();
        using var telemetry = new CrowCastTelemetry(() => members);

        telemetry.RecordPublished();
        telemetry.RecordReceived();
        telemetry.RecordDuplicate();
        telemetry.RecordExpired();
        telemetry.RecordRejectedMessage("invalid");
        telemetry.RecordRejectedMessage("rate_limit", rateLimited: true);
        telemetry.RecordRejectedMembershipUpdate("invalid");
        telemetry.RecordMembershipChange(CrowCastMemberStatus.Alive);
        telemetry.RecordCoordinatorChange();
        listener.RecordObservableInstruments();
        var snapshot = telemetry.Snapshot(members[0].Identity, members[0].Identity);

        Assert.Equal(
            Enum.GetNames<CrowCastMemberStatus>().OrderBy(value => value),
            observedStatuses.OrderBy(value => value));
        Assert.Equal(1, snapshot.AliveMembers);
        Assert.Equal(1, snapshot.SuspectMembers);
        Assert.Equal(1, snapshot.DeadMembers);
        Assert.Equal(1, snapshot.LeftMembers);
        Assert.Equal(2, snapshot.RejectedMessages);
        Assert.Equal(1, snapshot.RateLimitedMessages);
    }

    private static ServiceProvider CreateProvider(FakeCrowCastBackend backend)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICrowCastBackend>(backend);
        services.AddCrowCast();
        return services.BuildServiceProvider();
    }

    private sealed record UnknownNotification : CrowCastBackendNotification;

    private sealed class ThrowingMessageHandler : ICrowCastEventHandler<CrowCastMessageReceived>
    {
        public ValueTask HandleAsync(CrowCastMessageReceived @event, CancellationToken cancellationToken) =>
            throw new OperationCanceledException(cancellationToken);
    }
}
