using System.Text;
using CrowCast.Backends;
using CrowCast.Configuration;
using CrowCast.DependencyInjection;
using CrowCast.Events;
using CrowCast.Membership;
using CrowCast.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace CrowCast.Tests;

public sealed class CrowCastContractTests
{
    [Fact]
    public void Message_rejects_a_blank_type_and_copies_mutable_input()
    {
        Assert.Throws<ArgumentException>(() =>
            new CrowCastMessage(
                CrowCastMessageId.New(),
                " ",
                ReadOnlyMemory<byte>.Empty,
                DateTimeOffset.UtcNow));

        var content = Encoding.UTF8.GetBytes("before");
        var metadata = new Dictionary<string, string> { ["schema"] = "v1" };
        var message = CrowCastMessage.Create("  event.type  ", content, metadata);
        content[0] = 0;
        metadata["schema"] = "v2";

        Assert.Equal("event.type", message.Type);
        Assert.Equal("before", Encoding.UTF8.GetString(message.Content.Span));
        Assert.Equal("v1", message.Metadata["schema"]);
    }

    [Fact]
    public void Message_identifier_supports_creation_parsing_and_validation()
    {
        var created = CrowCastMessageId.New();
        var parsed = CrowCastMessageId.Parse(created.Value.ToString());

        Assert.Equal(created, parsed);
        Assert.Equal(created.Value.ToString("N"), created.ToString());
        Assert.Throws<ArgumentException>(() => new CrowCastMessageId(Guid.Empty));
    }

    [Fact]
    public void Node_identifiers_support_creation_parsing_comparison_and_validation()
    {
        var nodeId = CrowCastNodeId.New();
        var generation = CrowCastNodeGeneration.New();

        Assert.Equal(nodeId, CrowCastNodeId.Parse(nodeId.Value.ToString()));
        Assert.Equal(generation, CrowCastNodeGeneration.Parse(generation.Value.ToString()));
        Assert.Equal(0, nodeId.CompareTo(nodeId));
        Assert.Equal(0, generation.CompareTo(generation));
        Assert.Equal(nodeId.Value.ToString("N"), nodeId.ToString());
        Assert.Equal(generation.Value.ToString("N"), generation.ToString());
        Assert.Throws<ArgumentException>(() => new CrowCastNodeId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new CrowCastNodeGeneration(Guid.Empty));
    }

    [Fact]
    public void Node_identity_compares_node_then_generation_and_handles_null()
    {
        var node = new CrowCastNodeId(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var nextNode = new CrowCastNodeId(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var generation = new CrowCastNodeGeneration(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var nextGeneration = new CrowCastNodeGeneration(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var identity = new CrowCastNodeIdentity(node, generation);

        Assert.True(identity.CompareTo(null) > 0);
        Assert.True(identity.CompareTo(new CrowCastNodeIdentity(nextNode, generation)) < 0);
        Assert.True(identity.CompareTo(new CrowCastNodeIdentity(node, nextGeneration)) < 0);
        Assert.Equal($"{node}/{generation}", identity.ToString());
    }

    [Fact]
    public void Endpoint_trims_and_validates_its_values()
    {
        var endpoint = new CrowCastEndpoint(" host.local ", 4053);

        Assert.Equal("host.local", endpoint.Host);
        Assert.Equal(4053, endpoint.Port);
        Assert.Equal("host.local:4053", endpoint.ToString());
        Assert.Throws<ArgumentException>(() => new CrowCastEndpoint(" ", 4053));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrowCastEndpoint("host", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrowCastEndpoint("host", 65_536));
    }

    [Fact]
    public void Member_and_backend_context_reject_null_required_dependencies()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentNullException>(() =>
            new CrowCastMember(null!, CrowCastMemberStatus.Alive, now, now));

        var identity = CrowCastNodeIdentity.New();
        var settings = new CrowCastBackendSettings(
            "cluster",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3),
            new Dictionary<string, string>(),
            new CrowCastProtocolLimits(1, 1, 1, 1, 1, 1, TimeSpan.Zero));
        static ValueTask Handler(CrowCastBackendNotification notification, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        Assert.Throws<ArgumentNullException>(() => new CrowCastBackendContext(null!, settings, Handler));
        Assert.Throws<ArgumentNullException>(() => new CrowCastBackendContext(identity, null!, Handler));
        Assert.Throws<ArgumentNullException>(() => new CrowCastBackendContext(identity, settings, null!));

        var context = new CrowCastBackendContext(identity, settings, Handler);
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = context.NotifyAsync(null!);
        });
    }

    [Fact]
    public void Builder_registers_each_event_handler_once_and_returns_itself()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICrowCastBackend>(new FakeCrowCastBackend());
        var builder = services.AddCrowCast();

        Assert.Same(builder, builder.AddEventHandler<NoOpEventHandler>());
        Assert.Same(builder, builder.AddEventHandler<NoOpEventHandler>());

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<ICrowCastEventHandler>().OfType<NoOpEventHandler>());
    }

    private sealed class NoOpEventHandler : ICrowCastEventHandler
    {
        public ValueTask HandleAsync(CrowCastEvent @event, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
