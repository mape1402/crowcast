using System.Net;

namespace CrowCast.Backends;

public sealed class DnsCrowCastNodeDiscovery : ICrowCastNodeDiscovery
{
    private readonly string _hostName;
    private readonly int _port;
    private readonly Func<string, CancellationToken, ValueTask<IReadOnlyCollection<IPAddress>>> _resolver;

    public DnsCrowCastNodeDiscovery(string hostName, int port)
        : this(hostName, port, ResolveAsync)
    {
    }

    internal DnsCrowCastNodeDiscovery(
        string hostName,
        int port,
        Func<string, CancellationToken, ValueTask<IReadOnlyCollection<IPAddress>>> resolver)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            throw new ArgumentException("A DNS host name is required.", nameof(hostName));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The port must be between 1 and 65535.");
        }

        _hostName = hostName.Trim();
        _port = port;
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async ValueTask<IReadOnlyCollection<CrowCastEndpoint>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        var addresses = await _resolver(_hostName, cancellationToken).ConfigureAwait(false);
        return addresses
            .Select(address => new CrowCastEndpoint(address.ToString(), _port))
            .Distinct()
            .ToArray();
    }

    private static async ValueTask<IReadOnlyCollection<IPAddress>> ResolveAsync(
        string hostName,
        CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(hostName, cancellationToken).ConfigureAwait(false);
}
