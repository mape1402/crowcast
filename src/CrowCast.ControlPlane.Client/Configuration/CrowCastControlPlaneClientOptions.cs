namespace CrowCast.ControlPlane.Client.Configuration;

public sealed class CrowCastControlPlaneClientOptions
{
    public Uri? Endpoint { get; set; }

    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MinimumReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaximumReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    public int MaximumBatchSize { get; set; } = 256;
}
