using CrowCast.Akka.Internal;
using CrowCast.Akka.Transport;
using CrowCast.Backends;
using CrowCast.Configuration;
using CrowCast.Internal;
using CrowCast.Membership;
using CrowCast.Messaging;

namespace CrowCast.Tests;

public sealed class CrowCastInputValidationTests
{
    [Fact]
    public void Message_shape_validator_covers_every_rejection_reason()
    {
        var options = CreateOptions();
        AssertMessageRejected(CreateMessage("12345"), options, "type_too_large");
        AssertMessageRejected(CreateMessage(content: new byte[5]), options, "content_too_large");
        AssertMessageRejected(CreateMessage(metadata: TwoEntries()), options, "too_many_metadata_entries");
        AssertMessageRejected(CreateMessage(metadata: Metadata("", "v")), options, "invalid_metadata_key");
        AssertMessageRejected(CreateMessage(metadata: Metadata("1234", "v")), options, "metadata_key_too_large");
        AssertMessageRejected(CreateMessage(metadata: Metadata("k", null!)), options, "null_metadata_value");
        AssertMessageRejected(CreateMessage(metadata: Metadata("k", "1234")), options, "metadata_value_too_large");

        Assert.True(CrowCastInputValidator.TryValidateMessageShape(
            CreateMessage(metadata: Metadata("key", "123")),
            options,
            out var reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void Member_validator_covers_every_rejection_reason()
    {
        var options = CreateOptions();
        var now = DateTimeOffset.UtcNow;
        var identity = CrowCastNodeIdentity.New();

        AssertMemberRejected(
            new CrowCastMember(identity, (CrowCastMemberStatus)999, now, now),
            options,
            now,
            "invalid_status");
        AssertMemberRejected(
            new CrowCastMember(identity, CrowCastMemberStatus.Alive, now, now, " "),
            options,
            now,
            "invalid_endpoint");
        AssertMemberRejected(
            new CrowCastMember(identity, CrowCastMemberStatus.Alive, now, now, "12345"),
            options,
            now,
            "invalid_endpoint");
        AssertMemberRejected(
            new CrowCastMember(identity, CrowCastMemberStatus.Alive, now + TimeSpan.FromSeconds(2), now),
            options,
            now,
            "timestamp_too_far_in_future");
        AssertMemberRejected(
            new CrowCastMember(identity, CrowCastMemberStatus.Alive, now, now + TimeSpan.FromSeconds(2)),
            options,
            now,
            "timestamp_too_far_in_future");
        AssertMemberRejected(
            new CrowCastMember(identity, CrowCastMemberStatus.Alive, now, now - TimeSpan.FromSeconds(2)),
            options,
            now,
            "invalid_timestamp_order");
        AssertMemberRejected(
            new CrowCastMember(
                identity,
                CrowCastMemberStatus.Alive,
                now,
                now,
                metadata: TwoEntries()),
            options,
            now,
            "too_many_metadata_entries");

        Assert.True(CrowCastInputValidator.TryValidateMember(
            new CrowCastMember(identity, CrowCastMemberStatus.Alive, now, now, "node", Metadata("k", "v")),
            options,
            now,
            out var reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void Temporal_validation_accepts_boundaries_and_rejects_outside_them()
    {
        var options = CreateOptions();
        options.MessageTimeToLive = TimeSpan.FromSeconds(10);
        var now = DateTimeOffset.UtcNow;

        Assert.False(CrowCastInputValidator.IsTooFarInFuture(
            CreateMessage(createdAtUtc: now + options.MaximumClockSkew),
            options,
            now));
        Assert.True(CrowCastInputValidator.IsTooFarInFuture(
            CreateMessage(createdAtUtc: now + options.MaximumClockSkew + TimeSpan.FromTicks(1)),
            options,
            now));
        Assert.False(CrowCastInputValidator.IsExpired(
            CreateMessage(createdAtUtc: now - options.MessageTimeToLive + TimeSpan.FromTicks(1)),
            options,
            now));
        Assert.True(CrowCastInputValidator.IsExpired(
            CreateMessage(createdAtUtc: now - options.MessageTimeToLive),
            options,
            now));
    }

    [Fact]
    public void Wire_message_validator_covers_every_rejection_reason()
    {
        var valid = CreateEnvelope();

        AssertWireMessageRejected(valid with { MessageId = Guid.Empty }, "empty_message_id");
        AssertWireMessageRejected(valid with { Type = " " }, "invalid_message_type");
        AssertWireMessageRejected(valid with { Content = null! }, "missing_message_data");
        AssertWireMessageRejected(valid with { Metadata = null! }, "missing_message_data");
        AssertWireMessageRejected(valid with { OriginNodeId = Guid.NewGuid(), OriginGeneration = null }, "invalid_message_origin");
        AssertWireMessageRejected(valid with { OriginNodeId = Guid.Empty, OriginGeneration = Guid.NewGuid() }, "invalid_message_origin");
        AssertWireMessageRejected(valid with { OriginNodeId = Guid.NewGuid(), OriginGeneration = Guid.Empty }, "invalid_message_origin");

        Assert.True(CrowCastWireValidator.TryValidate(valid, out var reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void Wire_node_validator_covers_every_rejection_reason_and_leave_overload()
    {
        var limits = CreateLimits();
        var now = DateTimeOffset.UtcNow;
        var valid = new CrowCastNodeAnnouncement(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "node",
            now,
            Metadata("k", "v"));

        AssertWireNodeRejected(valid with { NodeId = Guid.Empty }, limits, now, "empty_node_identity");
        AssertWireNodeRejected(valid with { Generation = Guid.Empty }, limits, now, "empty_node_identity");
        AssertWireNodeRejected(valid with { Endpoint = " " }, limits, now, "invalid_endpoint");
        AssertWireNodeRejected(valid with { Endpoint = "12345" }, limits, now, "invalid_endpoint");
        AssertWireNodeRejected(
            valid with { StartedAtUtc = now + limits.MaximumClockSkew + TimeSpan.FromTicks(1) },
            limits,
            now,
            "timestamp_too_far_in_future");
        AssertWireNodeRejected(valid with { Metadata = null! }, limits, now, "invalid_metadata_count");
        AssertWireNodeRejected(valid with { Metadata = TwoEntries() }, limits, now, "invalid_metadata_count");
        AssertWireNodeRejected(valid with { Metadata = Metadata("", "v") }, limits, now, "invalid_metadata_entry");
        AssertWireNodeRejected(valid with { Metadata = Metadata("1234", "v") }, limits, now, "invalid_metadata_entry");
        AssertWireNodeRejected(valid with { Metadata = Metadata("k", null!) }, limits, now, "invalid_metadata_entry");
        AssertWireNodeRejected(valid with { Metadata = Metadata("k", "1234") }, limits, now, "invalid_metadata_entry");

        Assert.True(CrowCastWireValidator.TryValidate(valid, limits, now, out var announcementReason));
        Assert.Empty(announcementReason);

        var leaving = new CrowCastNodeLeaving(
            valid.NodeId,
            valid.Generation,
            valid.Endpoint,
            now,
            valid.Metadata);
        Assert.True(CrowCastWireValidator.TryValidate(leaving, limits, now, out var leavingReason));
        Assert.Empty(leavingReason);
        Assert.False(CrowCastWireValidator.TryValidate(
            leaving with { LeftAtUtc = now + TimeSpan.FromSeconds(2) },
            limits,
            now,
            out _));
    }

    private static CrowCastOptions CreateOptions() => new()
    {
        MaximumMessageTypeBytes = 4,
        MaximumMessageContentBytes = 4,
        MaximumMetadataEntries = 1,
        MaximumMetadataKeyBytes = 3,
        MaximumMetadataValueBytes = 3,
        MaximumEndpointBytes = 4,
        MaximumClockSkew = TimeSpan.FromSeconds(1)
    };

    private static CrowCastProtocolLimits CreateLimits() => new(
        MaximumMessageContentBytes: 4,
        MaximumMessageTypeBytes: 4,
        MaximumMetadataEntries: 1,
        MaximumMetadataKeyBytes: 3,
        MaximumMetadataValueBytes: 3,
        MaximumEndpointBytes: 4,
        MaximumClockSkew: TimeSpan.FromSeconds(1));

    private static CrowCastMessage CreateMessage(
        string type = "type",
        byte[]? content = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        DateTimeOffset? createdAtUtc = null) =>
        new(
            CrowCastMessageId.New(),
            type,
            content ?? [],
            createdAtUtc ?? DateTimeOffset.UtcNow,
            metadata);

    private static CrowCastMessageEnvelope CreateEnvelope() =>
        new(
            Guid.NewGuid(),
            "type",
            [],
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            null,
            null);

    private static Dictionary<string, string> Metadata(string key, string value) =>
        new(StringComparer.Ordinal) { [key] = value };

    private static Dictionary<string, string> TwoEntries() =>
        new(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2" };

    private static void AssertMessageRejected(CrowCastMessage message, CrowCastOptions options, string expectedReason)
    {
        Assert.False(CrowCastInputValidator.TryValidateMessageShape(message, options, out var reason));
        Assert.Equal(expectedReason, reason);
    }

    private static void AssertMemberRejected(
        CrowCastMember member,
        CrowCastOptions options,
        DateTimeOffset now,
        string expectedReason)
    {
        Assert.False(CrowCastInputValidator.TryValidateMember(member, options, now, out var reason));
        Assert.Equal(expectedReason, reason);
    }

    private static void AssertWireMessageRejected(CrowCastMessageEnvelope message, string expectedReason)
    {
        Assert.False(CrowCastWireValidator.TryValidate(message, out var reason));
        Assert.Equal(expectedReason, reason);
    }

    private static void AssertWireNodeRejected(
        CrowCastNodeAnnouncement announcement,
        CrowCastProtocolLimits limits,
        DateTimeOffset now,
        string expectedReason)
    {
        Assert.False(CrowCastWireValidator.TryValidate(announcement, limits, now, out var reason));
        Assert.Equal(expectedReason, reason);
    }
}
