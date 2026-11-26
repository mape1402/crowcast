using CrowCast.Observability;
using CrowCast.Observability.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace CrowCast.Tests.WebUI;

internal sealed class StubObservationSource(CrowCastObservationSnapshot snapshot) : ICrowCastObservationSource
{
    public CrowCastObservationSnapshot Snapshot { get; set; } = snapshot;
    public List<CrowCastObservationEvent> Events { get; } = [];
    public List<CrowCastObservationSample> Samples { get; } = [];
    public event Action? Changed;
    public CrowCastObservationSnapshot GetSnapshot() => Snapshot;
    public IReadOnlyList<CrowCastObservationEvent> GetEvents(long afterSequence = 0, int take = 200) => Events.Where(x => x.Sequence > afterSequence).Take(take).ToArray();
    public IReadOnlyList<CrowCastObservationSample> GetSamples(int take = 200) => Samples.TakeLast(take).ToArray();
    public void Raise() => Changed?.Invoke();
}

internal sealed class RecordingHubContext<THub> : IHubContext<THub> where THub : Hub
{
    public RecordingHubClients ClientsImpl { get; } = new();
    public RecordingGroupManager GroupsImpl { get; } = new();
    public IHubClients Clients => ClientsImpl;
    public IGroupManager Groups => GroupsImpl;
}

internal sealed class RecordingHubClients : IHubClients
{
    public RecordingClientProxy Proxy { get; } = new();
    public IClientProxy All => Proxy;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
    public IClientProxy Client(string connectionId) => Proxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
    public IClientProxy Group(string groupName) => Proxy;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
    public IClientProxy User(string userId) => Proxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
}

internal sealed class RecordingClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Calls { get; } = [];
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        Calls.Add((method, args));
        return Task.CompletedTask;
    }
}

internal sealed class RecordingGroupManager : IGroupManager
{
    public List<(string Connection, string Group)> Added { get; } = [];
    public List<(string Connection, string Group)> Removed { get; } = [];
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Removed.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
}

internal sealed class StubHubCallerContext(string connectionId = "connection") : HubCallerContext
{
    public override string ConnectionId => connectionId;
    public override string? UserIdentifier => null;
    public override System.Security.Claims.ClaimsPrincipal? User => null;
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() { }
}
