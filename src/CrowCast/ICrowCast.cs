using CrowCast.Membership;
using CrowCast.Messaging;

namespace CrowCast;

public interface ICrowCast
{
    CrowCastNodeIdentity LocalNode { get; }

    IReadOnlyCollection<CrowCastMember> Members { get; }

    CrowCastNodeIdentity? Coordinator { get; }

    ValueTask PublishAsync(CrowCastMessage message, CancellationToken cancellationToken = default);

    ValueTask PublishAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    ValueTask PublishAsync<TMessage>(
        TMessage message,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    ValueTask PublishToAllAsync(CrowCastMessage message, CancellationToken cancellationToken = default);

    ValueTask PublishToAllAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    ValueTask PublishToAllAsync<TMessage>(
        TMessage message,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    ValueTask PublishToMemberAsync(
        CrowCastMessage message,
        CrowCastNodeIdentity member,
        CancellationToken cancellationToken = default);

    ValueTask PublishToMemberAsync<TMessage>(
        TMessage message,
        CrowCastNodeIdentity member,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    ValueTask PublishToMemberAsync<TMessage>(
        TMessage message,
        CrowCastNodeIdentity member,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    ValueTask PublishToCoordinatorAsync(
        CrowCastMessage message,
        CancellationToken cancellationToken = default);

    ValueTask PublishToCoordinatorAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    ValueTask PublishToCoordinatorAsync<TMessage>(
        TMessage message,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;
}
