using Microsoft.Extensions.Options;

namespace CrowCast.ControlPlane.Client.Configuration;

internal sealed class CrowCastControlPlaneClientOptionsValidator : IValidateOptions<CrowCastControlPlaneClientOptions>
{
    public ValidateOptionsResult Validate(string? name, CrowCastControlPlaneClientOptions options)
    {
        if (options.Endpoint is null || !options.Endpoint.IsAbsoluteUri)
        {
            return ValidateOptionsResult.Fail("A valid absolute control plane Endpoint is required.");
        }

        if (options.SnapshotInterval <= TimeSpan.Zero || options.HeartbeatInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("SnapshotInterval and HeartbeatInterval must be positive.");
        }

        if (options.MinimumReconnectDelay <= TimeSpan.Zero || options.MaximumReconnectDelay < options.MinimumReconnectDelay)
        {
            return ValidateOptionsResult.Fail("Reconnect delays are invalid.");
        }

        return options.MaximumBatchSize is < 1 or > 1_000
            ? ValidateOptionsResult.Fail("MaximumBatchSize must be between 1 and 1000.")
            : ValidateOptionsResult.Success;
    }
}
