# CrowCast

CrowCast is an application-neutral gossip communication library for replicas of the same .NET service. It provides eventual membership, failure observations, message dissemination, local deduplication, coordinator notifications, metrics, and diagnostics without requiring a database or broker.

CrowCast does not execute application work, own messages, provide durable storage, or promise exactly-once delivery.

## Packages

- `CrowCast` contains the public API, models, backend and discovery abstractions, deduplication, event dispatch, coordination, and diagnostics. It has no Akka dependency.
- `CrowCast.Akka` implements the backend with Akka.NET Cluster and Distributed PubSub.

Applications depend on both packages while Akka is the selected backend. A future backend can implement `ICrowCastBackend` without changing consumer code.

## Replica identity

Every process receives a random node identifier and a random generation when CrowCast starts. Applications do not configure a fixed node name. The shared `ClusterName` identifies the service cluster, not an individual replica.

An origin attached to a message is contextual information only. It does not grant ownership, authority, or exclusivity over that message or related application work.

## Basic usage

```csharp
using CrowCast.Akka.DependencyInjection;
using CrowCast.Backends;
using CrowCast.DependencyInjection;

var crowCast = services.AddCrowCast(options =>
{
    options.ClusterName = "orders-api";
    options.ExchangeInterval = TimeSpan.FromSeconds(1);
    options.SuspectAfter = TimeSpan.FromSeconds(10);
    options.DeadAfter = TimeSpan.FromSeconds(30);
    options.DeduplicationWindow = TimeSpan.FromMinutes(5);
    options.MessageTimeToLive = TimeSpan.FromMinutes(5);
});

crowCast.UseAkka(options =>
{
    options.HostName = "0.0.0.0";
    options.Port = 4053;
    options.PublicHostName = "orders-api-0.orders-api";
    options.PublicPort = 4053;
});

crowCast.UseStaticNodeDiscovery(
    new CrowCastEndpoint("orders-api-0.orders-api", 4053));
crowCast.AddEventHandler<ApplicationCrowCastHandler>();
```

Publish an opaque payload with an application-defined type:

```csharp
var message = CrowCastMessage.Create(
    "partition.changed",
    JsonSerializer.SerializeToUtf8Bytes(payload),
    new Dictionary<string, string> { ["schema"] = "v1" });

await crowCast.PublishAsync(message, cancellationToken);
```

Handle membership, message, and coordinator notifications through `ICrowCastEventHandler`. Handlers must tolerate retries, duplicates outside the configured local deduplication window, and different observation order on different replicas.

## Membership semantics

CrowCast exposes these states:

- `Alive`: the replica participates and is reachable.
- `Suspect`: Akka's failure detector currently observes the replica as unreachable.
- `Dead`: the replica was downed or removed without an orderly CrowCast departure.
- `Left`: the replica announced an orderly departure.

`SuspectAfter` configures the acceptable heartbeat pause. `DeadAfter` includes the subsequent stable period before the split-brain resolver can remove a member. A later announcement can refute `Suspect`; `Dead` and `Left` are terminal for that node generation.

## Coordinator

When enabled, every replica deterministically selects the lowest random identity among its locally observed `Alive` members. Changes are emitted as `CrowCastCoordinatorChanged`.

This is a coordination hint, not a distributed lock or a strong-consensus leader. During partitions, replicas may temporarily disagree. Consumer operations must remain retryable and idempotent.

## Diagnostics

`ICrowCastDiagnostics.GetSnapshot()` returns membership counts and message, duplicate, expiration, membership-change, and coordinator-change counters. CrowCast also publishes the same operational signals through the `Elysium.CrowCast` `System.Diagnostics.Metrics` meter and uses `Microsoft.Extensions.Logging`.

## Example

Run two local replicas in separate terminals:

```powershell
dotnet run --project samples/CrowCast.Sample -- --port 4053 --seed-port 4053
dotnet run --project samples/CrowCast.Sample -- --port 4054 --seed-port 4053
```

Both replicas generate their identities at runtime, discover each other, exchange sample messages, and report the same coordinator after membership converges.

## Security status

Transport authentication, authorization, encryption, key rotation, and protection against hostile cluster members are deliberately deferred to phase 2. The current release must be used only on a trusted network boundary. `AdditionalConfiguration` can pass advanced Akka settings, but it is not a supported security contract yet.

See [architecture](docs/architecture.md) for protocol boundaries, extension points, and delivery semantics.

## Build and package

```powershell
dotnet restore CrowCast.sln
dotnet build CrowCast.sln --configuration Release
dotnet test CrowCast.sln --configuration Release
dotnet pack CrowCast.sln --configuration Release --output nupkgs
```

To release, add a non-empty `## [vX.Y.Z]` section to `CHANGELOG.md`, place the same tag in `.release`, and push to `main`. The repository must define `NUGET_USER` and a nuget.org Trusted Publishing policy.
