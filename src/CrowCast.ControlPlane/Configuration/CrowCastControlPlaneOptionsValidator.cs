using Microsoft.Extensions.Options;

namespace CrowCast.ControlPlane.Configuration;

internal sealed class CrowCastControlPlaneOptionsValidator : IValidateOptions<CrowCastControlPlaneOptions>
{
    public ValidateOptionsResult Validate(string? name, CrowCastControlPlaneOptions options)
    {
        if (options.ObserverStaleAfter <= TimeSpan.Zero || options.ObserverOfflineAfter <= options.ObserverStaleAfter)
        {
            return ValidateOptionsResult.Fail("ObserverOfflineAfter must be greater than a positive ObserverStaleAfter.");
        }

        if (options.MaximumEventsPerCluster is < 1 or > 1_000_000)
        {
            return ValidateOptionsResult.Fail("MaximumEventsPerCluster must be between 1 and 1000000.");
        }

        return options.MaximumObserversPerCluster is < 1 or > 100_000
            ? ValidateOptionsResult.Fail("MaximumObserversPerCluster must be between 1 and 100000.")
            : ValidateOptionsResult.Success;
    }
}
