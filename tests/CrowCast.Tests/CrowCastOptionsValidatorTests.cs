using CrowCast.Akka.Configuration;
using CrowCast.Configuration;

namespace CrowCast.Tests;

public sealed class CrowCastOptionsValidatorTests
{
    [Fact]
    public void Core_validator_reports_every_invalid_option_family()
    {
        var cases = new (Action<CrowCastOptions> Configure, string Failure)[]
        {
            (options => options.ClusterName = " ", "ClusterName is required"),
            (options => options.ClusterName = new string('x', 129), "ClusterName cannot exceed"),
            (options => options.ExchangeInterval = TimeSpan.Zero, "ExchangeInterval must be positive"),
            (options => options.SuspectAfter = options.ExchangeInterval, "SuspectAfter must be greater"),
            (options => options.DeadAfter = options.SuspectAfter, "DeadAfter must be greater"),
            (options => options.DeduplicationWindow = TimeSpan.Zero, "DeduplicationWindow must be positive"),
            (options => options.MessageTimeToLive = TimeSpan.Zero, "MessageTimeToLive must be positive"),
            (options =>
            {
                options.DeduplicationWindow = TimeSpan.FromSeconds(1);
                options.MessageTimeToLive = TimeSpan.FromSeconds(2);
            }, "DeduplicationWindow must be greater"),
            (options => options.MaximumDeduplicationEntries = 0, "MaximumDeduplicationEntries"),
            (options => options.MaximumMessageContentBytes = 0, "MaximumMessageContentBytes"),
            (options => options.MaximumMessageContentBytes = 16 * 1024 * 1024 + 1, "MaximumMessageContentBytes"),
            (options => options.MaximumMessageTypeBytes = 0, "MaximumMessageTypeBytes"),
            (options => options.MaximumMessageTypeBytes = 4 * 1024 + 1, "MaximumMessageTypeBytes"),
            (options => options.MaximumMetadataEntries = -1, "MaximumMetadataEntries"),
            (options => options.MaximumMetadataEntries = 1025, "MaximumMetadataEntries"),
            (options => options.MaximumMetadataKeyBytes = 0, "MaximumMetadataKeyBytes"),
            (options => options.MaximumMetadataKeyBytes = 4 * 1024 + 1, "MaximumMetadataKeyBytes"),
            (options => options.MaximumMetadataValueBytes = -1, "MaximumMetadataValueBytes"),
            (options => options.MaximumMetadataValueBytes = 64 * 1024 + 1, "MaximumMetadataValueBytes"),
            (options => options.MaximumEndpointBytes = 0, "MaximumEndpointBytes"),
            (options => options.MaximumEndpointBytes = 16 * 1024 + 1, "MaximumEndpointBytes"),
            (options => options.MaximumClockSkew = TimeSpan.FromTicks(-1), "MaximumClockSkew"),
            (options => options.MaximumReceivedMessagesPerInterval = 0, "MaximumReceivedMessagesPerInterval"),
            (options => options.MaximumReceivedMessagesPerInterval = 1_000_001, "MaximumReceivedMessagesPerInterval"),
            (options => options.ReceivedMessageRateLimitInterval = TimeSpan.Zero, "ReceivedMessageRateLimitInterval"),
            (options =>
            {
                options.MaximumMetadataEntries = 0;
                options.Metadata["key"] = "value";
            }, "Metadata cannot exceed"),
            (options => options.Metadata[""] = "value", "Metadata keys cannot be empty"),
            (options =>
            {
                options.MaximumMetadataKeyBytes = 1;
                options.Metadata["é"] = "value";
            }, "Metadata keys cannot exceed"),
            (options => options.Metadata["key"] = null!, "Metadata values cannot be null"),
            (options =>
            {
                options.MaximumMetadataValueBytes = 1;
                options.Metadata["key"] = "é";
            }, "Metadata values cannot be null")
        };

        var validator = new CrowCastOptionsValidator();
        foreach (var testCase in cases)
        {
            var options = new CrowCastOptions();
            testCase.Configure(options);

            var result = validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures, failure => failure.Contains(testCase.Failure, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Core_validator_accepts_defaults_and_inclusive_boundaries()
    {
        var options = new CrowCastOptions
        {
            MaximumMessageContentBytes = 16 * 1024 * 1024,
            MaximumMessageTypeBytes = 4 * 1024,
            MaximumMetadataEntries = 1024,
            MaximumMetadataKeyBytes = 4 * 1024,
            MaximumMetadataValueBytes = 64 * 1024,
            MaximumEndpointBytes = 16 * 1024,
            MaximumClockSkew = TimeSpan.Zero,
            MaximumReceivedMessagesPerInterval = 1_000_000
        };

        Assert.True(new CrowCastOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Akka_validator_reports_every_invalid_option_family()
    {
        var cases = new (Action<AkkaCrowCastOptions> Configure, string Failure)[]
        {
            (options => options.HostName = " ", "HostName is required"),
            (options => options.Port = -1, "Port must be zero"),
            (options => options.Port = 65_536, "Port must be zero"),
            (options => options.PublicPort = 0, "PublicPort must be between"),
            (options => options.PublicPort = 65_536, "PublicPort must be between"),
            (options => options.StartupTimeout = TimeSpan.Zero, "StartupTimeout must be positive"),
            (options => options.ShutdownTimeout = TimeSpan.Zero, "ShutdownTimeout must be positive"),
            (options => options.MaximumFrameSizeBytes = 31_999, "MaximumFrameSizeBytes"),
            (options => options.MaximumFrameSizeBytes = 64 * 1024 * 1024 + 1, "MaximumFrameSizeBytes")
        };

        var validator = new AkkaCrowCastOptionsValidator();
        foreach (var testCase in cases)
        {
            var options = new AkkaCrowCastOptions();
            testCase.Configure(options);

            var result = validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures, failure => failure.Contains(testCase.Failure, StringComparison.Ordinal));
        }

        Assert.True(validator.Validate(null, new AkkaCrowCastOptions()).Succeeded);
    }
}
