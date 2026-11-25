using System.Text;
using CrowCast.Events;
using Microsoft.Extensions.Logging;

internal sealed class SampleEventHandler(ILogger<SampleEventHandler> logger) : ICrowCastEventHandler
{
    public ValueTask HandleAsync(CrowCastEvent @event, CancellationToken cancellationToken)
    {
        switch (@event)
        {
            case CrowCastMemberChanged member:
                logger.LogInformation(
                    "Member {NodeId}/{Generation}: {PreviousStatus} -> {CurrentStatus} ({Endpoint})",
                    member.Current.Identity.NodeId,
                    member.Current.Identity.Generation,
                    member.Previous?.Status.ToString() ?? "new",
                    member.Current.Status,
                    member.Current.Endpoint);
                break;
            case CrowCastMessageReceived received:
                logger.LogInformation(
                    "Message {MessageId} type {MessageType} from {Origin}: {Content}",
                    received.Message.Id,
                    received.Message.Type,
                    received.Message.Origin,
                    Encoding.UTF8.GetString(received.Message.Content.Span));
                break;
            case CrowCastCoordinatorChanged coordinator:
                logger.LogInformation(
                    "Coordinator changed from {Previous} to {Current}",
                    coordinator.Previous,
                    coordinator.Current);
                break;
        }

        return ValueTask.CompletedTask;
    }
}
