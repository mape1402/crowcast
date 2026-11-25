using CrowCast.Messaging;

namespace CrowCast.Events;

public sealed record CrowCastMessageReceived(
    CrowCastMessage Message,
    DateTimeOffset OccurredAtUtc) : CrowCastEvent(OccurredAtUtc);
