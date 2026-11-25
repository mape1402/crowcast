using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using CrowCast.Akka.Transport;
using CrowCast.Backends;
using CrowCast.Membership;
using Microsoft.Extensions.Logging;

namespace CrowCast.Akka.Internal;

internal sealed class CrowCastClusterActor : ReceiveActor
{
    private readonly global::Akka.Cluster.Cluster _cluster;
    private readonly IActorRef _mediator;
    private readonly CrowCastBackendContext _backendContext;
    private readonly TaskCompletionSource _ready;
    private readonly ILogger _logger;
    private readonly string _topic;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, CrowCastNodeIdentity> _identitiesByEndpoint = new(StringComparer.Ordinal);
    private readonly Dictionary<CrowCastNodeIdentity, CrowCastNodeAnnouncement> _announcements = [];
    private readonly Dictionary<string, CrowCastMemberStatus> _statusByEndpoint = new(StringComparer.Ordinal);
    private readonly Dictionary<CrowCastNodeIdentity, CrowCastMemberStatus> _statusByIdentity = [];
    private ICancelable _announcementSchedule = Cancelable.CreateCanceled();
    private long _rejectedWireInputCount;

    public CrowCastClusterActor(
        CrowCastBackendContext backendContext,
        TaskCompletionSource ready,
        ILogger logger,
        DateTimeOffset startedAtUtc,
        TimeProvider timeProvider)
    {
        _backendContext = backendContext;
        _ready = ready;
        _logger = logger;
        _startedAtUtc = startedAtUtc;
        _timeProvider = timeProvider;
        _topic = $"crowcast-{backendContext.Settings.ClusterName}";
        _cluster = global::Akka.Cluster.Cluster.Get(Context.System);
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        Receive<SubscribeAck>(_ => HandleSubscribeAck());
        Receive<PublishAnnouncement>(_ => PublishLocalAnnouncement());
        Receive<PublishMessage>(message =>
            _mediator.Tell(new Publish(_topic, CrowCastWireMapper.ToEnvelope(message.Message))));
        Receive<PrepareToLeave>(HandlePrepareToLeave);

        ReceiveAsync<global::Akka.Cluster.ClusterEvent.CurrentClusterState>(HandleCurrentStateAsync);
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberUp>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Alive));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.ReachableMember>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Alive));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.UnreachableMember>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Suspect));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberLeft>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Left));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberExited>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Left));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberDowned>(message =>
            UpdateClusterStatusAsync(message.Member, CrowCastMemberStatus.Dead));
        ReceiveAsync<global::Akka.Cluster.ClusterEvent.MemberRemoved>(HandleMemberRemovedAsync);

        ReceiveAsync<CrowCastNodeAnnouncement>(HandleAnnouncementAsync);
        ReceiveAsync<CrowCastNodeLeaving>(HandleLeavingAsync);
        ReceiveAsync<CrowCastMessageEnvelope>(HandleMessageAsync);
    }

    protected override void PreStart()
    {
        _cluster.Subscribe(
            Self,
            global::Akka.Cluster.ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot,
            typeof(global::Akka.Cluster.ClusterEvent.IMemberEvent),
            typeof(global::Akka.Cluster.ClusterEvent.IReachabilityEvent));
        _mediator.Tell(new Subscribe(_topic, Self));
        _announcementSchedule = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            TimeSpan.Zero,
            _backendContext.Settings.ExchangeInterval,
            Self,
            PublishAnnouncement.Instance,
            Self);
        base.PreStart();
    }

    protected override void PostStop()
    {
        _announcementSchedule.Cancel();
        _mediator.Tell(new Unsubscribe(_topic, Self));
        _cluster.Unsubscribe(Self);
        base.PostStop();
    }

    private void HandleSubscribeAck()
    {
        PublishLocalAnnouncement();
        _ready.TrySetResult();
    }

    private void PublishLocalAnnouncement()
    {
        var identity = _backendContext.LocalNode;
        var announcement = new CrowCastNodeAnnouncement(
            identity.NodeId.Value,
            identity.Generation.Value,
            _cluster.SelfAddress.ToString(),
            _startedAtUtc,
            _backendContext.Settings.Metadata);
        _mediator.Tell(new Publish(_topic, announcement));
    }

    private void HandlePrepareToLeave(PrepareToLeave message)
    {
        var identity = _backendContext.LocalNode;
        _mediator.Tell(new Publish(
            _topic,
            new CrowCastNodeLeaving(
                identity.NodeId.Value,
                identity.Generation.Value,
                _cluster.SelfAddress.ToString(),
                _timeProvider.GetUtcNow(),
                _backendContext.Settings.Metadata)));
        message.Completion.TrySetResult();
    }

    private async Task HandleCurrentStateAsync(global::Akka.Cluster.ClusterEvent.CurrentClusterState state)
    {
        foreach (var member in state.Members)
        {
            var status = member.Status switch
            {
                global::Akka.Cluster.MemberStatus.Up or global::Akka.Cluster.MemberStatus.WeaklyUp => CrowCastMemberStatus.Alive,
                global::Akka.Cluster.MemberStatus.Leaving or global::Akka.Cluster.MemberStatus.Exiting => CrowCastMemberStatus.Left,
                global::Akka.Cluster.MemberStatus.Down or global::Akka.Cluster.MemberStatus.Removed => CrowCastMemberStatus.Dead,
                _ => CrowCastMemberStatus.Suspect
            };
            await UpdateClusterStatusAsync(member, status).ConfigureAwait(false);
        }

        foreach (var member in state.Unreachable)
        {
            await UpdateClusterStatusAsync(member, CrowCastMemberStatus.Suspect).ConfigureAwait(false);
        }
    }

    private Task HandleMemberRemovedAsync(global::Akka.Cluster.ClusterEvent.MemberRemoved message)
    {
        var status = message.PreviousStatus is global::Akka.Cluster.MemberStatus.Leaving or global::Akka.Cluster.MemberStatus.Exiting
            ? CrowCastMemberStatus.Left
            : CrowCastMemberStatus.Dead;
        return UpdateClusterStatusAsync(message.Member, status);
    }

    private Task HandleAnnouncementAsync(CrowCastNodeAnnouncement announcement)
    {
        if (!CrowCastWireValidator.TryValidate(
                announcement,
                _backendContext.Settings.Limits,
                _timeProvider.GetUtcNow(),
                out var rejectionReason))
        {
            LogRejectedWireInput("node_announcement", rejectionReason);
            return Task.CompletedTask;
        }

        var identity = new CrowCastNodeIdentity(
            new CrowCastNodeId(announcement.NodeId),
            new CrowCastNodeGeneration(announcement.Generation));

        if (_statusByIdentity.TryGetValue(identity, out var identityStatus) &&
            identityStatus is CrowCastMemberStatus.Dead or CrowCastMemberStatus.Left)
        {
            return NotifyMemberAsync(identity, announcement, identityStatus);
        }

        _identitiesByEndpoint.TryGetValue(announcement.Endpoint, out var previousIdentity);
        if (previousIdentity is not null &&
            previousIdentity != identity &&
            _announcements.TryGetValue(previousIdentity, out var previousAnnouncement) &&
            previousAnnouncement.StartedAtUtc > announcement.StartedAtUtc)
        {
            return Task.CompletedTask;
        }

        _identitiesByEndpoint[announcement.Endpoint] = identity;
        _announcements[identity] = announcement;

        // Terminal identities returned above; a new generation at the same endpoint is alive.
        const CrowCastMemberStatus status = CrowCastMemberStatus.Alive;
        _statusByEndpoint[announcement.Endpoint] = status;

        _statusByIdentity[identity] = status;

        return NotifyMemberAsync(identity, announcement, status);
    }

    private Task HandleLeavingAsync(CrowCastNodeLeaving leaving)
    {
        if (!CrowCastWireValidator.TryValidate(
                leaving,
                _backendContext.Settings.Limits,
                _timeProvider.GetUtcNow(),
                out var rejectionReason))
        {
            LogRejectedWireInput("node_leaving", rejectionReason);
            return Task.CompletedTask;
        }

        var identity = new CrowCastNodeIdentity(
            new CrowCastNodeId(leaving.NodeId),
            new CrowCastNodeGeneration(leaving.Generation));
        var announcement = new CrowCastNodeAnnouncement(
            leaving.NodeId,
            leaving.Generation,
            leaving.Endpoint,
            leaving.LeftAtUtc,
            leaving.Metadata);

        _identitiesByEndpoint[leaving.Endpoint] = identity;
        _announcements[identity] = announcement;
        _statusByEndpoint[leaving.Endpoint] = CrowCastMemberStatus.Left;
        _statusByIdentity[identity] = CrowCastMemberStatus.Left;
        return NotifyMemberAsync(identity, announcement, CrowCastMemberStatus.Left);
    }

    private Task HandleMessageAsync(CrowCastMessageEnvelope envelope)
    {
        if (!CrowCastWireValidator.TryValidate(envelope, out var rejectionReason))
        {
            LogRejectedWireInput("message", rejectionReason);
            return Task.CompletedTask;
        }

        return _backendContext.NotifyAsync(
            new CrowCastBackendMessageReceived(CrowCastWireMapper.ToMessage(envelope)),
            CancellationToken.None).AsTask();
    }

    private Task UpdateClusterStatusAsync(global::Akka.Cluster.Member member, CrowCastMemberStatus status)
    {
        var endpoint = member.Address.ToString();
        if (_statusByEndpoint.TryGetValue(endpoint, out var currentStatus) &&
            currentStatus is CrowCastMemberStatus.Dead or CrowCastMemberStatus.Left)
        {
            status = currentStatus;
        }

        _statusByEndpoint[endpoint] = status;

        if (!_identitiesByEndpoint.TryGetValue(endpoint, out var identity) ||
            !_announcements.TryGetValue(identity, out var announcement))
        {
            return Task.CompletedTask;
        }

        if (_statusByIdentity.TryGetValue(identity, out var identityStatus) &&
            identityStatus is CrowCastMemberStatus.Dead or CrowCastMemberStatus.Left)
        {
            status = identityStatus;
        }

        _statusByIdentity[identity] = status;

        return NotifyMemberAsync(identity, announcement, status);
    }

    private Task NotifyMemberAsync(
        CrowCastNodeIdentity identity,
        CrowCastNodeAnnouncement announcement,
        CrowCastMemberStatus status)
    {
        _logger.LogDebug(
            "CrowCast observed node {NodeId} generation {Generation} as {Status}",
            identity.NodeId,
            identity.Generation,
            status);

        var member = new CrowCastMember(
            identity,
            status,
            announcement.StartedAtUtc,
            _timeProvider.GetUtcNow(),
            announcement.Endpoint,
            announcement.Metadata);
        return _backendContext.NotifyAsync(
            new CrowCastBackendMemberChanged(member),
            CancellationToken.None).AsTask();
    }

    private void LogRejectedWireInput(string inputType, string reason)
    {
        var count = ++_rejectedWireInputCount;
        if (count == 1 || count % 100 == 0)
        {
            _logger.LogWarning(
                "CrowCast rejected {InputType} wire input because {Reason}; rejected wire input count is {RejectedCount}",
                inputType,
                reason,
                count);
        }
    }

    internal sealed record PublishMessage(CrowCast.Messaging.CrowCastMessage Message);

    internal sealed record PrepareToLeave(TaskCompletionSource Completion);

    private sealed class PublishAnnouncement
    {
        public static PublishAnnouncement Instance { get; } = new();
    }
}
