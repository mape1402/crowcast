using CrowCast.Messaging;

namespace CrowCast.Backends;

public sealed record CrowCastBackendMessageReceived(CrowCastMessage Message) : CrowCastBackendNotification;
