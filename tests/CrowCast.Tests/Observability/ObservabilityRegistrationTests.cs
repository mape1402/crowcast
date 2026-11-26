using CrowCast.Events;
using CrowCast.Observability;
using CrowCast.Observability.DependencyInjection;
using CrowCast.Observability.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrowCast.Tests.Observability;

public sealed class ObservabilityRegistrationTests
{
    [Fact]
    public void Registration_validates_arguments_and_reuses_single_source()
    {
        Assert.Throws<ArgumentNullException>(() => CrowCastObservabilityServiceCollectionExtensions.AddCrowCastObservability(null!));
        var services = new ServiceCollection();
        services.AddCrowCastObservability(options => options.ServiceName = "test");
        var source = services.Single(descriptor => descriptor.ServiceType == typeof(ICrowCastObservationSource));
        var handlers = services.Where(descriptor =>
            descriptor.ServiceType.IsGenericType &&
            descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICrowCastEventHandler<>)).ToArray();
        var hosted = services.Last(descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.NotNull(source.ImplementationFactory);
        Assert.Equal(3, handlers.Length);
        Assert.All(handlers, handler => Assert.NotNull(handler.ImplementationType));
        Assert.NotNull(hosted.ImplementationType);
    }

    [Fact]
    public void Options_validator_covers_sampling_and_buffer_bounds()
    {
        var validator = new CrowCastObservabilityOptionsValidator();
        Assert.Equal(ValidateOptionsResult.Success, validator.Validate(null, new()));
        Assert.True(validator.Validate(null, new() { SamplingInterval = TimeSpan.Zero }).Failed);
        Assert.True(validator.Validate(null, new() { MaximumEvents = 0 }).Failed);
        Assert.True(validator.Validate(null, new() { MaximumEvents = 100_001 }).Failed);
        Assert.True(validator.Validate(null, new() { MaximumSamples = 0 }).Failed);
        Assert.True(validator.Validate(null, new() { MaximumSamples = 100_001 }).Failed);
    }
}
