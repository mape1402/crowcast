using CrowCast.Backends;
using CrowCast.Membership;
using CrowCast.Messaging;

namespace CrowCast.Tests;

internal sealed class FakeCrowCastBackend : ICrowCastBackend
{
    public Exception? StartException { get; set; }

    public Exception? PublishException { get; set; }

    public Exception? StopException { get; set; }

    public bool AnnounceLocalNodeOnStart { get; set; }

    public CrowCastBackendContext? Context { get; private set; }

    public List<CrowCastMessage> PublishedMessages { get; } = [];

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int DisposeCount { get; private set; }

    public ValueTask StartAsync(CrowCastBackendContext context, CancellationToken cancellationToken)
    {
        StartCount++;
        if (StartException is not null)
        {
            throw StartException;
        }

        Context = context;
        if (AnnounceLocalNodeOnStart)
        {
            var now = DateTimeOffset.UtcNow;
            return context.NotifyAsync(new CrowCastBackendMemberChanged(
                new CrowCastMember(context.LocalNode, CrowCastMemberStatus.Alive, now, now)));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask PublishAsync(CrowCastMessage message, CancellationToken cancellationToken)
    {
        if (PublishException is not null)
        {
            throw PublishException;
        }

        PublishedMessages.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        StopCount++;
        if (StopException is not null)
        {
            throw StopException;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask EmitAsync(
        CrowCastBackendNotification notification,
        CancellationToken cancellationToken = default) =>
        (Context ?? throw new InvalidOperationException("Backend not started."))
            .NotifyAsync(notification, cancellationToken);
}
