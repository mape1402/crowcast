namespace CrowCast.Backends;

public readonly record struct CrowCastEndpoint
{
    public CrowCastEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("A host is required.", nameof(host));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The port must be between 1 and 65535.");
        }

        Host = host.Trim();
        Port = port;
    }

    public string Host { get; }

    public int Port { get; }

    public override string ToString() => $"{Host}:{Port}";
}
