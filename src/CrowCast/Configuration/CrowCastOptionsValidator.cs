using Microsoft.Extensions.Options;
using System.Text;

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

        if (options.MaximumMessageContentBytes is < 1 or > 16 * 1024 * 1024)
        {
            failures.Add("MaximumMessageContentBytes must be between 1 byte and 16 MiB.");
        }

        if (options.MaximumMessageTypeBytes is < 1 or > 4 * 1024)
        {
            failures.Add("MaximumMessageTypeBytes must be between 1 byte and 4 KiB.");
        }

        if (options.MaximumMetadataEntries is < 0 or > 1024)
        {
            failures.Add("MaximumMetadataEntries must be between 0 and 1024.");
        }

        if (options.MaximumMetadataKeyBytes is < 1 or > 4 * 1024)
        {
            failures.Add("MaximumMetadataKeyBytes must be between 1 byte and 4 KiB.");
        }

        if (options.MaximumMetadataValueBytes is < 0 or > 64 * 1024)
        {
            failures.Add("MaximumMetadataValueBytes must be between 0 bytes and 64 KiB.");
        }

        if (options.MaximumEndpointBytes is < 1 or > 16 * 1024)
        {
            failures.Add("MaximumEndpointBytes must be between 1 byte and 16 KiB.");
        }

        if (options.MaximumClockSkew < TimeSpan.Zero)
        {
            failures.Add("MaximumClockSkew cannot be negative.");
        }

        if (options.MaximumReceivedMessagesPerInterval is < 1 or > 1_000_000)
        {
            failures.Add("MaximumReceivedMessagesPerInterval must be between 1 and 1000000.");
        }

        if (options.ReceivedMessageRateLimitInterval <= TimeSpan.Zero)
        {
            failures.Add("ReceivedMessageRateLimitInterval must be positive.");
        }

        if (options.Metadata.Count > options.MaximumMetadataEntries)
        {
            failures.Add("Metadata cannot exceed MaximumMetadataEntries.");
        }

        if (options.Metadata.Any(pair => string.IsNullOrWhiteSpace(pair.Key)))
        {
            failures.Add("Metadata keys cannot be empty.");
        }

        if (options.Metadata.Any(pair => Utf8Length(pair.Key) > options.MaximumMetadataKeyBytes))
        {
            failures.Add("Metadata keys cannot exceed MaximumMetadataKeyBytes.");
        }

        if (options.Metadata.Any(pair => pair.Value is null || Utf8Length(pair.Value) > options.MaximumMetadataValueBytes))
        {
            failures.Add("Metadata values cannot be null or exceed MaximumMetadataValueBytes.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static int Utf8Length(string value) => Encoding.UTF8.GetByteCount(value);
}
