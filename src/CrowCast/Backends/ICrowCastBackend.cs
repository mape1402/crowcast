using CrowCast.Messaging;

namespace CrowCast.Backends;

public interface ICrowCastBackend : IAsyncDisposable
{
    ValueTask StartAsync(CrowCastBackendContext context, CancellationToken cancellationToken);

    ValueTask PublishAsync(
        CrowCastMessage message,
        CrowCastMessageTarget target,
        CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}
