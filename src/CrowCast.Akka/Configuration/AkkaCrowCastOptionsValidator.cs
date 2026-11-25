using Microsoft.Extensions.Options;

namespace CrowCast.Akka.Configuration;

internal sealed class AkkaCrowCastOptionsValidator : IValidateOptions<AkkaCrowCastOptions>
{
    public ValidateOptionsResult Validate(string? name, AkkaCrowCastOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.HostName))
        {
            failures.Add("HostName is required.");
        }

        if (options.Port is < 0 or > 65535)
        {
            failures.Add("Port must be zero for a dynamic port or between 1 and 65535.");
        }

        if (options.PublicPort is < 1 or > 65535)
        {
            failures.Add("PublicPort must be between 1 and 65535 when specified.");
        }

        if (options.StartupTimeout <= TimeSpan.Zero)
        {
            failures.Add("StartupTimeout must be positive.");
        }

        if (options.ShutdownTimeout <= TimeSpan.Zero)
        {
            failures.Add("ShutdownTimeout must be positive.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
