using CrowCast.Backends;
using CrowCast.Messaging;

namespace CrowCast.Tests;

internal sealed class FakeCrowCastBackend : ICrowCastBackend
{
    public CrowCastBackendContext? Context { get; private set; }

    public List<CrowCastMessage> PublishedMessages { get; } = [];

    public ValueTask StartAsync(CrowCastBackendContext context, CancellationToken cancellationToken)
    {
        Context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask PublishAsync(CrowCastMessage message, CancellationToken cancellationToken)
    {
        PublishedMessages.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask EmitAsync(CrowCastBackendNotification notification) =>
        (Context ?? throw new InvalidOperationException("Backend not started."))
            .NotifyAsync(notification);
}
