namespace CrowCast.Events;

public abstract record CrowCastEvent(DateTimeOffset OccurredAtUtc);
