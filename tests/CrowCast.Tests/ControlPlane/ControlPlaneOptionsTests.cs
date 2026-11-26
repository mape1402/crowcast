using CrowCast.ControlPlane.Client.Configuration;
using CrowCast.ControlPlane.Configuration;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests.ControlPlane;

public sealed class ControlPlaneOptionsTests
{
    private readonly CrowCastControlPlaneClientOptionsValidator _validator = new();
    private readonly CrowCastControlPlaneOptionsValidator _serverValidator = new();

    [Fact]
    public void Validator_accepts_valid_configuration()
    {
        var options = Valid();
        Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, options));
    }

    [Theory]
    [InlineData("endpoint")]
    [InlineData("snapshot-interval")]
    [InlineData("heartbeat-interval")]
    [InlineData("minimum-reconnect")]
    [InlineData("maximum-reconnect")]
    [InlineData("batch-low")]
    [InlineData("batch-high")]
    public void Validator_rejects_invalid_configuration(string scenario)
    {
        var options = Valid();
        switch (scenario)
        {
            case "endpoint": options.Endpoint = null; break;
            case "snapshot-interval": options.SnapshotInterval = TimeSpan.Zero; break;
            case "heartbeat-interval": options.HeartbeatInterval = TimeSpan.Zero; break;
            case "minimum-reconnect": options.MinimumReconnectDelay = TimeSpan.Zero; break;
            case "maximum-reconnect": options.MaximumReconnectDelay = TimeSpan.FromMilliseconds(1); break;
            case "batch-low": options.MaximumBatchSize = 0; break;
            case "batch-high": options.MaximumBatchSize = 1_001; break;
        }
        Assert.True(_validator.Validate(null, options).Failed);
    }

    private static CrowCastControlPlaneClientOptions Valid() => new() { Endpoint = new Uri("http://localhost:5081") };

    [Fact]
    public void Server_validator_covers_retention_and_freshness_bounds()
    {
        Assert.Equal(ValidateOptionsResult.Success, _serverValidator.Validate(null, new CrowCastControlPlaneOptions()));
        Assert.True(_serverValidator.Validate(null, new() { ObserverStaleAfter = TimeSpan.Zero }).Failed);
        Assert.True(_serverValidator.Validate(null, new() { ObserverOfflineAfter = TimeSpan.FromSeconds(10) }).Failed);
        Assert.True(_serverValidator.Validate(null, new() { MaximumEventsPerCluster = 0 }).Failed);
        Assert.True(_serverValidator.Validate(null, new() { MaximumEventsPerCluster = 1_000_001 }).Failed);
        Assert.True(_serverValidator.Validate(null, new() { MaximumObserversPerCluster = 0 }).Failed);
        Assert.True(_serverValidator.Validate(null, new() { MaximumObserversPerCluster = 100_001 }).Failed);
    }
}
