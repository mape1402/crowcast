namespace CrowCast.Backends;

public interface ICrowCastNodeDiscovery
{
    ValueTask<IReadOnlyCollection<CrowCastEndpoint>> DiscoverAsync(CancellationToken cancellationToken);
}
