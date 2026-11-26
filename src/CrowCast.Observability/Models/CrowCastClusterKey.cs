namespace CrowCast.Observability.Models;

public sealed record CrowCastClusterKey(string Environment, string Service, string Cluster, string Scope)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Scope)
        ? $"{Environment}/{Service}/{Cluster}"
        : $"{Environment}/{Scope}/{Service}/{Cluster}";
}
