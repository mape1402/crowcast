namespace CrowCast.ControlPlane.Configuration;

public sealed class CrowCastControlPlaneOptions
{
    public TimeSpan ObserverStaleAfter { get; set; } = TimeSpan.FromSeconds(20);

    public TimeSpan ObserverOfflineAfter { get; set; } = TimeSpan.FromMinutes(2);

    public int MaximumEventsPerCluster { get; set; } = 10_000;

    public int MaximumObserversPerCluster { get; set; } = 10_000;
}
