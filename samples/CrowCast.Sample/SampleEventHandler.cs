using System.Text;
using CrowCast.Events;
using Microsoft.Extensions.Logging;

internal sealed class SampleMemberChangedHandler(ILogger<SampleMemberChangedHandler> logger) :
    ICrowCastEventHandler<CrowCastMemberChanged>
{
    public ValueTask HandleAsync(CrowCastMemberChanged @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Member {NodeId}/{Generation}: {PreviousStatus} -> {CurrentStatus} ({Endpoint})",
            @event.Current.Identity.NodeId,
            @event.Current.Identity.Generation,
            @event.Previous?.Status.ToString() ?? "new",
            @event.Current.Status,
            @event.Current.Endpoint);

        return ValueTask.CompletedTask;
    }
}

internal sealed class SampleMessageReceivedHandler(ILogger<SampleMessageReceivedHandler> logger) :
    ICrowCastEventHandler<CrowCastMessageReceived>
{
    public ValueTask HandleAsync(CrowCastMessageReceived @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Message {MessageId} type {MessageType} from {Origin}: {Content}",
            @event.Message.Id,
            @event.Message.Type,
            @event.Message.Origin,
            Encoding.UTF8.GetString(@event.Message.Content.Span));

        return ValueTask.CompletedTask;
    }
}

internal sealed class SampleCoordinatorChangedHandler(ILogger<SampleCoordinatorChangedHandler> logger) :
    ICrowCastEventHandler<CrowCastCoordinatorChanged>
{
    public ValueTask HandleAsync(CrowCastCoordinatorChanged @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Coordinator changed from {Previous} to {Current}",
            @event.Previous,
            @event.Current);

        return ValueTask.CompletedTask;
    }
}
