namespace CrowCast.Observability.Configuration;

public sealed class CrowCastObservabilityOptions
{
    public string? ServiceName { get; set; }

    public string? EnvironmentName { get; set; }

    public string? Scope { get; set; }

    public TimeSpan SamplingInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int MaximumEvents { get; set; } = 2_048;

    public int MaximumSamples { get; set; } = 720;

    public bool IncludeEndpoints { get; set; }

    public ISet<string> IncludedMetadataKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
}
