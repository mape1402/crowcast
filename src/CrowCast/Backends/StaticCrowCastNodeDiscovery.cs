namespace CrowCast.Backends;

public sealed class StaticCrowCastNodeDiscovery : ICrowCastNodeDiscovery
{
    private readonly IReadOnlyCollection<CrowCastEndpoint> _endpoints;

    public StaticCrowCastNodeDiscovery(IEnumerable<CrowCastEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        _endpoints = endpoints.Distinct().ToArray();
    }

    public ValueTask<IReadOnlyCollection<CrowCastEndpoint>> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_endpoints);
    }
}
