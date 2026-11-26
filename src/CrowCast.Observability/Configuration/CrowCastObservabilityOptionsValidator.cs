using Microsoft.Extensions.Options;

namespace CrowCast.Observability.Configuration;

internal sealed class CrowCastObservabilityOptionsValidator : IValidateOptions<CrowCastObservabilityOptions>
{
    public ValidateOptionsResult Validate(string? name, CrowCastObservabilityOptions options)
    {
        if (options.SamplingInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("SamplingInterval must be positive.");
        }

        if (options.MaximumEvents is < 1 or > 100_000)
        {
            return ValidateOptionsResult.Fail("MaximumEvents must be between 1 and 100000.");
        }

        return options.MaximumSamples is < 1 or > 100_000
            ? ValidateOptionsResult.Fail("MaximumSamples must be between 1 and 100000.")
            : ValidateOptionsResult.Success;
    }
}
