using CrowCast.Events;
using CrowCast.Messaging;
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
    ICrowCastMessageHandler<SampleHeartbeat>
{
    public ValueTask HandleAsync(
        CrowCastMessageContext<SampleHeartbeat> context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Message {MessageId} type {MessageType} from {Origin}: {Content}",
            context.Envelope.Id,
            context.Envelope.Type,
            context.Origin,
            context.Message.Text);

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
