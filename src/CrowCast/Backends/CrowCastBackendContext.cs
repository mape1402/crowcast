using CrowCast.Membership;

namespace CrowCast.Backends;

public sealed class CrowCastBackendContext
{
    private readonly Func<CrowCastBackendNotification, CancellationToken, ValueTask> _notificationHandler;

    public CrowCastBackendContext(
        CrowCastNodeIdentity localNode,
        CrowCastBackendSettings settings,
        Func<CrowCastBackendNotification, CancellationToken, ValueTask> notificationHandler)
    {
        LocalNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notificationHandler = notificationHandler ?? throw new ArgumentNullException(nameof(notificationHandler));
    }

    public CrowCastNodeIdentity LocalNode { get; }

    public CrowCastBackendSettings Settings { get; }

    public ValueTask NotifyAsync(
        CrowCastBackendNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return _notificationHandler(notification, cancellationToken);
    }
}
