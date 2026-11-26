using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using CrowCast.Backends;
using CrowCast.DependencyInjection;
using CrowCast.Internal;
using CrowCast.Membership;
using CrowCast.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrowCast.Tests;

public sealed class TypedMessageTests
{
    [Fact]
    public void Builder_registers_stable_message_contracts_handlers_and_custom_serializers_once()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICrowCastBackend>(new FakeCrowCastBackend());
        services.AddSingleton<TypedMessageRecorder>();
        var builder = services.AddCrowCast();

        Assert.Throws<ArgumentException>(() => builder.AddMessage<TestPayload>(" "));
        Assert.Same(builder, builder.AddMessage<TestPayload>(" test.payload.v1 "));
        Assert.Same(builder, builder.AddMessage<TestPayload>("test.payload.v1"));
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddMessage<TestPayload>("test.payload.v2"));
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddMessage<OtherPayload>("test.payload.v1"));
        Assert.Same(
            builder,
            builder.AddMessageHandler<TestPayload, RecordingTypedHandler>("test.payload.v1"));
        Assert.Same(
            builder,
            builder.AddMessageHandler<TestPayload, RecordingTypedHandler>("test.payload.v1"));
        Assert.Same(builder, builder.UseMessageSerializer<PrefixMessageSerializer>());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var contract = Assert.Single(provider.GetServices<ICrowCastMessageContract>());
        Assert.Equal("test.payload.v1", contract.MessageType);
        Assert.Equal(typeof(TestPayload), contract.PayloadType);
        Assert.IsType<PrefixMessageSerializer>(provider.GetRequiredService<ICrowCastMessageSerializer>());
        using var scope = provider.CreateScope();
        Assert.Single(scope.ServiceProvider.GetServices<ICrowCastMessageHandler<TestPayload>>());
    }

    [Fact]
    public void System_text_json_serializer_round_trips_payloads_and_rejects_null_results()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SystemTextJsonCrowCastMessageSerializer(null!));
        var serializer = new SystemTextJsonCrowCastMessageSerializer();

        var bytes = serializer.Serialize(new TestPayload("hello"));
        var result = serializer.Deserialize<TestPayload>(bytes);

        Assert.Contains("\"text\"", Encoding.UTF8.GetString(bytes));
        Assert.Equal("hello", result.Text);
        Assert.Throws<ArgumentNullException>(() => serializer.Serialize<string>(null!));
        Assert.Throws<JsonException>(() =>
            serializer.Deserialize<TestPayload>(Encoding.UTF8.GetBytes("null")));
    }

    [Fact]
    public async Task Typed_publish_supports_alias_all_member_coordinator_and_metadata()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(
            configureBuilder: builder => builder.AddMessage<TestPayload>("test.payload.v1"));
        var member = CrowCastNodeIdentity.New();
        var metadata = new Dictionary<string, string> { ["trace"] = "abc" };

        await runtime.CrowCast.PublishAsync(new TestPayload("alias"));
        await runtime.CrowCast.PublishAsync(new TestPayload("alias-meta"), metadata);
        await runtime.CrowCast.PublishToAllAsync(new TestPayload("all"));
        await runtime.CrowCast.PublishToAllAsync(new TestPayload("all-meta"), metadata);
        await runtime.CrowCast.PublishToMemberAsync(new TestPayload("member"), member);
        await runtime.CrowCast.PublishToMemberAsync(new TestPayload("member-meta"), member, metadata);
        await runtime.CrowCast.PublishToCoordinatorAsync(new TestPayload("coordinator"));
        await runtime.CrowCast.PublishToCoordinatorAsync(new TestPayload("coordinator-meta"), metadata);

        var serializer = new SystemTextJsonCrowCastMessageSerializer();
        Assert.Equal(8, runtime.Backend.PublishedMessages.Count);
        Assert.All(runtime.Backend.PublishedMessages, message =>
        {
            Assert.Equal("test.payload.v1", message.Type);
            Assert.Equal(runtime.CrowCast.LocalNode, message.Origin);
        });
        Assert.Equal(
            new[] { "alias", "alias-meta", "all", "all-meta", "member", "member-meta", "coordinator", "coordinator-meta" },
            runtime.Backend.PublishedMessages
                .Select(message => serializer.Deserialize<TestPayload>(message.Content).Text));
        Assert.Empty(runtime.Backend.PublishedMessages[0].Metadata);
        Assert.Equal("abc", runtime.Backend.PublishedMessages[1].Metadata["trace"]);
        Assert.Collection(
            runtime.Backend.PublishedTargets,
            target => Assert.True(target.IsAllMembers),
            target => Assert.True(target.IsAllMembers),
            target => Assert.True(target.IsAllMembers),
            target => Assert.True(target.IsAllMembers),
            target => Assert.Equal(member, target.Member),
            target => Assert.Equal(member, target.Member),
            target => Assert.Equal(runtime.CrowCast.LocalNode, target.Member),
            target => Assert.Equal(runtime.CrowCast.LocalNode, target.Member));
    }

    [Fact]
    public async Task Typed_publish_validates_registration_payload_member_and_coordinator()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(
            options => options.EnableCoordinatorElection = false,
            configureBuilder: builder => builder.AddMessage<TestPayload>("test.payload.v1"));

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await runtime.CrowCast.PublishToAllAsync<TestPayload>(null!));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.CrowCast.PublishToAllAsync(new OtherPayload(1)));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await runtime.CrowCast.PublishToMemberAsync(new TestPayload("member"), null!));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.CrowCast.PublishToCoordinatorAsync(new TestPayload("coordinator")));
        Assert.Empty(runtime.Backend.PublishedMessages);
    }

    [Fact]
    public async Task Typed_messages_are_deserialized_and_delivered_with_their_envelope_context()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(
            configureServices: services => services.AddSingleton<TypedMessageRecorder>(),
            configureBuilder: builder =>
                builder.AddMessageHandler<TestPayload, RecordingTypedHandler>("test.payload.v1"));
        var serializer = new SystemTextJsonCrowCastMessageSerializer();
        var origin = CrowCastNodeIdentity.New();
        var envelope = new CrowCastMessage(
            CrowCastMessageId.New(),
            "test.payload.v1",
            serializer.Serialize(new TestPayload("received")),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["schema"] = "v1" },
            origin);

        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(envelope));

        var context = Assert.Single(runtime.GetRequiredService<TypedMessageRecorder>().Contexts);
        Assert.Equal("received", context.Message.Text);
        Assert.Same(envelope, context.Envelope);
        Assert.Same(runtime.CrowCast, context.CrowCast);
        Assert.Equal(origin, context.Origin);
        Assert.Equal("v1", context.Metadata["schema"]);
        Assert.NotEqual(default, context.OccurredAtUtc);
    }

    [Fact]
    public async Task Unknown_unhandled_and_malformed_typed_messages_preserve_raw_delivery()
    {
        await using var unhandledRuntime = await CrowCastRuntimeHarness.StartAsync(
            configureBuilder: builder => builder.AddMessage<TestPayload>("test.payload.v1"));
        var unknown = CrowCastMessage.Create("unknown.v1", Encoding.UTF8.GetBytes("{}"));
        var unhandled = CrowCastMessage.Create(
            "test.payload.v1",
            JsonSerializer.SerializeToUtf8Bytes(new TestPayload("ignored")));

        await unhandledRuntime.Backend.EmitAsync(new CrowCastBackendMessageReceived(unknown));
        await unhandledRuntime.Backend.EmitAsync(new CrowCastBackendMessageReceived(unhandled));
        Assert.Equal(2, unhandledRuntime.Events.Events.OfType<Events.CrowCastMessageReceived>().Count());

        await using var malformedRuntime = await CrowCastRuntimeHarness.StartAsync(
            configureServices: services => services.AddSingleton<TypedMessageRecorder>(),
            configureBuilder: builder =>
                builder.AddMessageHandler<TestPayload, RecordingTypedHandler>("test.payload.v1"));
        var malformed = CrowCastMessage.Create("test.payload.v1", Encoding.UTF8.GetBytes("{"));

        await malformedRuntime.Backend.EmitAsync(new CrowCastBackendMessageReceived(malformed));

        Assert.Single(malformedRuntime.Events.Events.OfType<Events.CrowCastMessageReceived>());
        Assert.Empty(malformedRuntime.GetRequiredService<TypedMessageRecorder>().Contexts);
    }

    [Fact]
    public async Task Typed_handler_failures_are_isolated_and_requested_cancellation_propagates()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(
            configureServices: services => services.AddSingleton<TypedMessageRecorder>(),
            configureBuilder: builder =>
            {
                builder.AddMessageHandler<TestPayload, ThrowingTypedHandler>("test.payload.v1");
                builder.AddMessageHandler<TestPayload, RecordingTypedHandler>("test.payload.v1");
                builder.AddMessageHandler<TestPayload, CancelingTypedHandler>("test.payload.v1");
            });
        var message = CrowCastMessage.Create(
            "test.payload.v1",
            JsonSerializer.SerializeToUtf8Bytes(new TestPayload("failure")));

        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(message));
        Assert.Single(runtime.GetRequiredService<TypedMessageRecorder>().Contexts);

        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await runtime.Backend.EmitAsync(
                new CrowCastBackendMessageReceived(CrowCastMessage.Create(
                    "test.payload.v1",
                    JsonSerializer.SerializeToUtf8Bytes(new TestPayload("canceled")))),
                source.Token));
    }

    [Fact]
    public async Task Custom_serializer_is_used_for_typed_publish_and_receive()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync(
            configureServices: services => services.AddSingleton<TypedMessageRecorder>(),
            configureBuilder: builder =>
            {
                builder.UseMessageSerializer<PrefixMessageSerializer>();
                builder.AddMessageHandler<TestPayload, RecordingTypedHandler>("test.payload.v1");
            });

        await runtime.CrowCast.PublishToAllAsync(new TestPayload("custom"));
        var published = Assert.Single(runtime.Backend.PublishedMessages);
        Assert.Equal("prefix:custom", Encoding.UTF8.GetString(published.Content.Span));

        await runtime.Backend.EmitAsync(new CrowCastBackendMessageReceived(published));
        Assert.Equal(
            "custom",
            Assert.Single(runtime.GetRequiredService<TypedMessageRecorder>().Contexts).Message.Text);
    }

    [Fact]
    public async Task Message_context_and_internal_contract_validate_required_values()
    {
        await using var runtime = await CrowCastRuntimeHarness.StartAsync();
        var envelope = CrowCastMessage.Create("test.payload.v1", ReadOnlyMemory<byte>.Empty);

        Assert.Throws<ArgumentNullException>(() =>
            new CrowCastMessageContext<TestPayload>(null!, envelope, DateTimeOffset.UtcNow, runtime.CrowCast));
        Assert.Throws<ArgumentNullException>(() =>
            new CrowCastMessageContext<TestPayload>(new("value"), null!, DateTimeOffset.UtcNow, runtime.CrowCast));
        Assert.Throws<ArgumentNullException>(() =>
            new CrowCastMessageContext<TestPayload>(new("value"), envelope, DateTimeOffset.UtcNow, null!));

        var contract = new CrowCastMessageContract<TestPayload>(
            "test.payload.v1",
            new SystemTextJsonCrowCastMessageSerializer(),
            runtime.GetRequiredService<IServiceScopeFactory>());
        Assert.Throws<ArgumentException>(() =>
            contract.CreateMessage(new OtherPayload(1), metadata: null));
    }

    private sealed record TestPayload(string Text);

    private sealed record OtherPayload(int Value);

    private sealed class TypedMessageRecorder
    {
        public ConcurrentQueue<CrowCastMessageContext<TestPayload>> Contexts { get; } = new();
    }

    private sealed class RecordingTypedHandler(TypedMessageRecorder recorder) :
        ICrowCastMessageHandler<TestPayload>
    {
        public ValueTask HandleAsync(
            CrowCastMessageContext<TestPayload> context,
            CancellationToken cancellationToken)
        {
            recorder.Contexts.Enqueue(context);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingTypedHandler : ICrowCastMessageHandler<TestPayload>
    {
        public ValueTask HandleAsync(
            CrowCastMessageContext<TestPayload> context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("handler failed");
    }

    private sealed class CancelingTypedHandler : ICrowCastMessageHandler<TestPayload>
    {
        public ValueTask HandleAsync(
            CrowCastMessageContext<TestPayload> context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PrefixMessageSerializer : ICrowCastMessageSerializer
    {
        public byte[] Serialize<TMessage>(TMessage message)
            where TMessage : notnull =>
            Encoding.UTF8.GetBytes($"prefix:{Assert.IsType<TestPayload>(message).Text}");

        public TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> content)
            where TMessage : notnull
        {
            var text = Encoding.UTF8.GetString(content.Span);
            return (TMessage)(object)new TestPayload(text["prefix:".Length..]);
        }
    }
}
