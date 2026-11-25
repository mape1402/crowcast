using System.Text;
using System.Text.Json;
using Akka.Actor;
using CrowCast.Akka.Configuration;
using CrowCast.Akka.Internal;
using CrowCast.Akka.Serialization;
using CrowCast.Akka.Transport;
using CrowCast.Backends;

namespace CrowCast.Tests;

public sealed class AkkaConfigurationAndSerializationTests
{
    [Fact]
    public void Actor_system_name_is_normalized_and_has_a_safe_fallback()
    {
        Assert.Equal("crowcast-orders-api", AkkaCrowCastConfiguration.GetActorSystemName(" Orders.API "));
        Assert.Equal("crowcast", AkkaCrowCastConfiguration.GetActorSystemName("---"));
        Assert.Equal("crowcast-a_b-1", AkkaCrowCastConfiguration.GetActorSystemName("A_B-1"));
    }

    [Fact]
    public void Configuration_applies_network_defaults_public_address_and_explicit_overrides()
    {
        var settings = CreateBackendSettings();
        var options = new AkkaCrowCastOptions
        {
            HostName = "0.0.0.0",
            Port = 4053,
            PublicHostName = "node\\\"one",
            PublicPort = 4054,
            AdditionalConfiguration = "akka.loglevel = DEBUG"
        };

        var configuration = AkkaCrowCastConfiguration.Create(settings, options);

        Assert.Equal("DEBUG", configuration.GetString("akka.loglevel"));
        Assert.Equal("0.0.0.0", configuration.GetString("akka.remote.dot-netty.tcp.hostname"));
        Assert.Equal("node\\\"one", configuration.GetString("akka.remote.dot-netty.tcp.public-hostname"));
        Assert.Equal(4054, configuration.GetInt("akka.remote.dot-netty.tcp.public-port"));
        Assert.False(configuration.GetBoolean("akka.actor.serialization-settings.allow-unregistered-types"));
        Assert.Equal(256 * 1024, configuration.GetByteSize("akka.remote.dot-netty.tcp.maximum-frame-size"));
    }

    [Fact]
    public void Configuration_uses_bind_address_when_public_address_is_absent()
    {
        var configuration = AkkaCrowCastConfiguration.Create(
            CreateBackendSettings(),
            new AkkaCrowCastOptions());

        Assert.Null(configuration.GetString("akka.remote.dot-netty.tcp.public-hostname", null));
        Assert.Null(configuration.GetString("akka.remote.dot-netty.tcp.public-port", null));
    }

    [Fact]
    public void Configuration_rejects_frames_that_cannot_hold_protocol_limits()
    {
        var standardOptions = new AkkaCrowCastOptions { MaximumFrameSizeBytes = 32_000 };
        Assert.Throws<InvalidOperationException>(() =>
            AkkaCrowCastConfiguration.Create(CreateBackendSettings(), standardOptions));

        var hugeLimits = new CrowCastProtocolLimits(
            MaximumMessageContentBytes: 1,
            MaximumMessageTypeBytes: 1,
            MaximumMetadataEntries: 10_000,
            MaximumMetadataKeyBytes: 50_000,
            MaximumMetadataValueBytes: 50_000,
            MaximumEndpointBytes: 1,
            MaximumClockSkew: TimeSpan.Zero);
        var hugeSettings = CreateBackendSettings(hugeLimits);
        var maximumOptions = new AkkaCrowCastOptions { MaximumFrameSizeBytes = 64 * 1024 * 1024 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AkkaCrowCastConfiguration.Create(hugeSettings, maximumOptions));
        Assert.Contains(int.MaxValue.ToString(), exception.Message);
    }

    [Fact]
    public async Task Serializer_round_trips_all_wire_contracts_and_rejects_unknown_types()
    {
        var system = ActorSystem.Create($"serializer-{Guid.NewGuid():N}");
        try
        {
            var serializer = new CrowCastAkkaSerializer((ExtendedActorSystem)system);
            var announcement = new CrowCastNodeAnnouncement(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "akka.tcp://node",
                DateTimeOffset.UtcNow,
                new Dictionary<string, string> { ["zone"] = "one" });
            var leaving = new CrowCastNodeLeaving(
                announcement.NodeId,
                announcement.Generation,
                announcement.Endpoint,
                announcement.StartedAtUtc,
                announcement.Metadata);
            var envelope = new CrowCastMessageEnvelope(
                Guid.NewGuid(),
                "event",
                [1, 2, 3],
                DateTimeOffset.UtcNow,
                new Dictionary<string, string> { ["schema"] = "v1" },
                announcement.NodeId,
                announcement.Generation);

            AssertRoundTrip<CrowCastNodeAnnouncement>(serializer, announcement);
            AssertRoundTrip<CrowCastNodeLeaving>(serializer, leaving);
            AssertRoundTrip<CrowCastMessageEnvelope>(serializer, envelope);

            Assert.Throws<ArgumentException>(() => serializer.Manifest(new object()));
            Assert.Throws<ArgumentException>(() => serializer.ToBinary(new object()));
            Assert.Throws<ArgumentException>(() => serializer.FromBinary([], "unknown-manifest"));
            Assert.Throws<JsonException>(() =>
                serializer.FromBinary(Encoding.UTF8.GetBytes("null"), "crowcast-message-v1"));

            var jsonWithUnknownMember = Encoding.UTF8.GetString(serializer.ToBinary(envelope));
            jsonWithUnknownMember = jsonWithUnknownMember.Insert(jsonWithUnknownMember.Length - 1, ",\"Unknown\":true");
            Assert.Throws<JsonException>(() =>
                serializer.FromBinary(Encoding.UTF8.GetBytes(jsonWithUnknownMember), "crowcast-message-v1"));
        }
        finally
        {
            await system.Terminate();
        }
    }

    private static CrowCastBackendSettings CreateBackendSettings(CrowCastProtocolLimits? limits = null) =>
        new(
            "orders",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            new Dictionary<string, string>(),
            limits ?? new CrowCastProtocolLimits(
                MaximumMessageContentBytes: 64 * 1024,
                MaximumMessageTypeBytes: 128,
                MaximumMetadataEntries: 32,
                MaximumMetadataKeyBytes: 64,
                MaximumMetadataValueBytes: 512,
                MaximumEndpointBytes: 2048,
                MaximumClockSkew: TimeSpan.FromSeconds(30)));

    private static void AssertRoundTrip<T>(CrowCastAkkaSerializer serializer, T value)
        where T : class
    {
        var manifest = serializer.Manifest(value);
        var bytes = serializer.ToBinary(value);
        var result = serializer.FromBinary(bytes, manifest);

        Assert.IsType<T>(result);
    }
}
