namespace CrowCast.Configuration;

public sealed class CrowCastOptions
{
    public string ClusterName { get; set; } = "crowcast";

    public TimeSpan ExchangeInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan SuspectAfter { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan DeadAfter { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan MessageTimeToLive { get; set; } = TimeSpan.FromMinutes(5);

    public int MaximumDeduplicationEntries { get; set; } = 100_000;

    public bool EnableCoordinatorElection { get; set; } = true;

    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
