namespace CrowCast.Backends;

public sealed record CrowCastProtocolLimits(
    int MaximumMessageContentBytes,
    int MaximumMessageTypeBytes,
    int MaximumMetadataEntries,
    int MaximumMetadataKeyBytes,
    int MaximumMetadataValueBytes,
    int MaximumEndpointBytes,
    TimeSpan MaximumClockSkew);
