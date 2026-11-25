using Microsoft.Extensions.Options;

namespace CrowCast.Configuration;

internal sealed class CrowCastOptionsValidator : IValidateOptions<CrowCastOptions>
{
    public ValidateOptionsResult Validate(string? name, CrowCastOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClusterName))
        {
            failures.Add("ClusterName is required.");
        }
        else if (options.ClusterName.Length > 128)
        {
            failures.Add("ClusterName cannot exceed 128 characters.");
        }

        if (options.ExchangeInterval <= TimeSpan.Zero)
        {
            failures.Add("ExchangeInterval must be positive.");
        }

        if (options.SuspectAfter <= options.ExchangeInterval)
        {
            failures.Add("SuspectAfter must be greater than ExchangeInterval.");
        }

        if (options.DeadAfter <= options.SuspectAfter)
        {
            failures.Add("DeadAfter must be greater than SuspectAfter.");
        }

        if (options.DeduplicationWindow <= TimeSpan.Zero)
        {
            failures.Add("DeduplicationWindow must be positive.");
        }

        if (options.MessageTimeToLive <= TimeSpan.Zero)
        {
            failures.Add("MessageTimeToLive must be positive.");
        }
        else if (options.DeduplicationWindow < options.MessageTimeToLive)
        {
            failures.Add("DeduplicationWindow must be greater than or equal to MessageTimeToLive.");
        }

        if (options.MaximumDeduplicationEntries < 1)
        {
            failures.Add("MaximumDeduplicationEntries must be at least 1.");
        }

        if (options.Metadata.Any(pair => string.IsNullOrWhiteSpace(pair.Key)))
        {
            failures.Add("Metadata keys cannot be empty.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
