using CrowCast.Configuration;
using CrowCast.Diagnostics;
using CrowCast.Events;
using CrowCast.Membership;
using CrowCast.Observability.Configuration;
using CrowCast.Observability.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrowCast.Observability.Internal;

internal sealed class CrowCastObservationSource : BackgroundService, ICrowCastObservationSource
{
    private readonly ICrowCast _crowCast;
    private readonly ICrowCastDiagnostics _diagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly CrowCastObservabilityOptions _options;
    private readonly CrowCastClusterKey _clusterKey;
    private readonly CrowCastObservationEventSink _eventSink;
    private readonly BoundedObservationBuffer<CrowCastObservationSample> _samples;

    public CrowCastObservationSource(
        ICrowCast crowCast,
        ICrowCastDiagnostics diagnostics,
        IOptions<CrowCastOptions> crowCastOptions,
        IOptions<CrowCastObservabilityOptions> options,
        IHostEnvironment environment,
        TimeProvider timeProvider,
        CrowCastObservationEventSink eventSink)
    {
        _crowCast = crowCast;
        _diagnostics = diagnostics;
        _timeProvider = timeProvider;
        _options = options.Value;
        _eventSink = eventSink;
        _eventSink.Changed += NotifyChanged;
        _clusterKey = new CrowCastClusterKey(
            Normalize(_options.EnvironmentName, environment.EnvironmentName),
            Normalize(_options.ServiceName, environment.ApplicationName),
            Normalize(crowCastOptions.Value.ClusterName, "crowcast"),
            _options.Scope?.Trim() ?? string.Empty);
        _samples = new(_options.MaximumSamples);
    }

    public event Action? Changed;

    public CrowCastObservationSnapshot GetSnapshot()
    {
        var diagnostics = _diagnostics.GetSnapshot();
        var coordinator = _crowCast.Coordinator is null ? null : CrowCastObserverIdentity.From(_crowCast.Coordinator);
        var members = _crowCast.Members.Select(member => MapMember(member, coordinator)).ToArray();
        return new(
            1,
            _clusterKey,
            CrowCastObserverIdentity.From(_crowCast.LocalNode),
            _eventSink.Sequence,
            _timeProvider.GetUtcNow(),
            coordinator,
            members,
            CrowCastTelemetryCounters.From(diagnostics));
    }

    public IReadOnlyList<CrowCastObservationEvent> GetEvents(long afterSequence = 0, int take = 200) =>
        _eventSink.GetEvents(afterSequence, take);

    public IReadOnlyList<CrowCastObservationSample> GetSamples(int take = 200) =>
        _samples.Snapshot().TakeLast(Math.Clamp(take, 1, 2_000)).ToArray();

    public ValueTask HandleAsync(CrowCastEvent @event, CancellationToken cancellationToken)
    {
        return _eventSink.HandleAsync(@event, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Sample();
        using var timer = new PeriodicTimer(_options.SamplingInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            Sample();
        }
    }

    private void Sample()
    {
        var snapshot = GetSnapshot();
        _samples.Add(new(
            snapshot.ObservedAtUtc,
            snapshot.Members.Count(member => member.Status == nameof(CrowCastMemberStatus.Alive)),
            snapshot.Members.Count(member => member.Status == nameof(CrowCastMemberStatus.Suspect)),
            snapshot.Members.Count(member => member.Status == nameof(CrowCastMemberStatus.Dead)),
            snapshot.Members.Count(member => member.Status == nameof(CrowCastMemberStatus.Left)),
            snapshot.Counters));
        NotifyChanged();
    }

    private CrowCastMemberObservation MapMember(CrowCastMember member, CrowCastObserverIdentity? coordinator)
    {
        var identity = CrowCastObserverIdentity.From(member.Identity);
        var metadata = member.Metadata
            .Where(pair => _options.IncludedMetadataKeys.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new(
            identity,
            member.Status.ToString(),
            member.FirstSeenAtUtc,
            member.LastChangedAtUtc,
            _options.IncludeEndpoints ? member.Endpoint : null,
            metadata,
            identity == coordinator,
            member.Identity == _crowCast.LocalNode);
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    public override void Dispose()
    {
        _eventSink.Changed -= NotifyChanged;
        base.Dispose();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
