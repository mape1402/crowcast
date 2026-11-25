namespace CrowCast.Backends;

public sealed record CrowCastBackendSettings(
    string ClusterName,
    TimeSpan ExchangeInterval,
    TimeSpan SuspectAfter,
    TimeSpan DeadAfter,
    IReadOnlyDictionary<string, string> Metadata,
    CrowCastProtocolLimits Limits);
