using System.Collections.Immutable;
using Akka.Actor;
using CrowCast.Akka.Configuration;
using CrowCast.Backends;
using CrowCast.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrowCast.Akka.Internal;

internal sealed class AkkaCrowCastBackend : ICrowCastBackend
{
    private readonly ICrowCastNodeDiscovery _discovery;
    private readonly AkkaCrowCastOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AkkaCrowCastBackend> _logger;
    private readonly TimeProvider _timeProvider;
    private ActorSystem? _actorSystem;
    private global::Akka.Cluster.Cluster? _cluster;
    private IActorRef? _clusterActor;
    private CrowCastBackendContext? _context;

    public AkkaCrowCastBackend(
        ICrowCastNodeDiscovery discovery,
        IOptions<AkkaCrowCastOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<AkkaCrowCastBackend> logger,
        TimeProvider timeProvider)
    {
        _discovery = discovery;
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async ValueTask StartAsync(CrowCastBackendContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_actorSystem is not null)
        {
            throw new InvalidOperationException("The Akka CrowCast backend has already been started.");
        }

        _context = context;
        var systemName = AkkaCrowCastConfiguration.GetActorSystemName(context.Settings.ClusterName);
        var configuration = AkkaCrowCastConfiguration.Create(context.Settings, _options);
        var actorSystem = ActorSystem.Create(systemName, configuration);
        var cluster = global::Akka.Cluster.Cluster.Get(actorSystem);
        _actorSystem = actorSystem;
        _cluster = cluster;

        try
        {
            var endpoints = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            if (endpoints.Count == 0)
            {
                cluster.Join(cluster.SelfAddress);
                _logger.LogInformation("CrowCast is bootstrapping a new Akka cluster at {Address}", cluster.SelfAddress);
            }
            else
            {
                var seedNodes = endpoints
                    .Select(endpoint => new Address("akka.tcp", systemName, endpoint.Host, endpoint.Port))
                    .ToImmutableList();
                cluster.JoinSeedNodes(seedNodes);
                _logger.LogInformation("CrowCast is joining Akka seed nodes {SeedNodes}", seedNodes);
            }

            var memberReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var subscriptionReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actor = actorSystem.ActorOf(
                Props.Create(() => new CrowCastClusterActor(
                    context,
                    memberReady,
                    subscriptionReady,
                    _loggerFactory.CreateLogger<CrowCastClusterActor>(),
                    _timeProvider.GetUtcNow(),
                    _timeProvider)),
                "crowcast");
            _clusterActor = actor;

            await Task.WhenAll(memberReady.Task, subscriptionReady.Task)
                .WaitAsync(_options.StartupTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await actorSystem.Terminate().ConfigureAwait(false);
            _actorSystem = null;
            _cluster = null;
            _clusterActor = null;
            _context = null;
            throw;
        }
    }

    public ValueTask PublishAsync(
        CrowCastMessage message,
        CrowCastMessageTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var actor = _clusterActor ??
            throw new InvalidOperationException("The Akka CrowCast backend has not been started.");
        actor.Tell(new CrowCastClusterActor.PublishMessage(message, target));
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        var actorSystem = _actorSystem;
        var cluster = _cluster;
        var actor = _clusterActor;
        if (actorSystem is null)
        {
            return;
        }

        try
        {
            if (actor is not null)
            {
                var leavePublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                actor.Tell(new CrowCastClusterActor.PrepareToLeave(leavePublished));
                await leavePublished.Task.WaitAsync(_options.ShutdownTimeout, cancellationToken).ConfigureAwait(false);

                var propagationDelay = _context is null
                    ? TimeSpan.FromMilliseconds(100)
                    : TimeSpan.FromMilliseconds(Math.Min(500, _context.Settings.ExchangeInterval.TotalMilliseconds));
                await Task.Delay(propagationDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            cluster?.Leave(cluster.SelfAddress);

            var gracefulWait = TimeSpan.FromMilliseconds(
                Math.Min(5_000, _options.ShutdownTimeout.TotalMilliseconds / 2));
            var gracefulShutdown = actorSystem.WhenTerminated;
            var completed = await Task.WhenAny(
                    gracefulShutdown,
                    Task.Delay(gracefulWait, _timeProvider, cancellationToken))
                .ConfigureAwait(false);

            if (completed == gracefulShutdown)
            {
                await gracefulShutdown.ConfigureAwait(false);
            }
            else
            {
                await actorSystem.Terminate()
                    .WaitAsync(_options.ShutdownTimeout - gracefulWait, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(exception, "Timed out while gracefully stopping the Akka CrowCast backend");
            await actorSystem.Terminate().ConfigureAwait(false);
        }
        finally
        {
            _actorSystem = null;
            _cluster = null;
            _clusterActor = null;
            _context = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_actorSystem is not null)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
