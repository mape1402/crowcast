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

    public int MaximumMessageContentBytes { get; set; } = 64 * 1024;

    public int MaximumMessageTypeBytes { get; set; } = 128;

    public int MaximumMetadataEntries { get; set; } = 32;

    public int MaximumMetadataKeyBytes { get; set; } = 64;

    public int MaximumMetadataValueBytes { get; set; } = 512;

    public int MaximumEndpointBytes { get; set; } = 2 * 1024;

    public TimeSpan MaximumClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    public int MaximumReceivedMessagesPerInterval { get; set; } = 10_000;

    public TimeSpan ReceivedMessageRateLimitInterval { get; set; } = TimeSpan.FromSeconds(1);

    public bool EnableCoordinatorElection { get; set; } = true;

    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
