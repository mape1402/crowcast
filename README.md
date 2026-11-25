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
    options.MaximumMessageContentBytes = 64 * 1024;
    options.MaximumReceivedMessagesPerInterval = 10_000;
    options.ReceivedMessageRateLimitInterval = TimeSpan.FromSeconds(1);
});

crowCast.UseAkka(options =>
{
    options.HostName = "0.0.0.0";
    options.Port = 4053;
    options.PublicHostName = Environment.GetEnvironmentVariable("POD_IP");
    options.PublicPort = 4053;
});

crowCast.UseDnsNodeDiscovery("orders-api-crowcast", 4053);
crowCast.AddEventHandler<ApplicationCrowCastHandler>();
```

`orders-api-crowcast` is a stable DNS name for the workload, not a replica identity. In Kubernetes, use a headless Service with `publishNotReadyAddresses: true`, inject the pod IP through the downward API, and advertise that IP through `PublicHostName`. Each startup resolves the current pod addresses while the CrowCast node ID and generation remain random. The template in [`deploy/kubernetes/headless-service.yaml`](deploy/kubernetes/headless-service.yaml) contains the required Service shape.

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

When Akka removes a member, the backend releases its endpoint and announcement state immediately. CrowCast retains only the newest terminal members for diagnostics and late-message suppression, bounded by `MaximumRetainedTerminalMembers` (1,024 by default). Set it to zero when the application needs only membership-change events and no terminal history in `Members`.

Backend startup completes only after Akka reports the local member as `Up`; subscribing to the local pub/sub mediator alone is not treated as cluster readiness.

## Coordinator

When enabled, every replica deterministically selects the lowest random identity among its locally observed `Alive` members. Changes are emitted as `CrowCastCoordinatorChanged`.

This is a coordination hint, not a distributed lock or a strong-consensus leader. During partitions, replicas may temporarily disagree. Consumer operations must remain retryable and idempotent.

## Diagnostics

`ICrowCastDiagnostics.GetSnapshot()` returns membership counts and message, duplicate, expiration, membership-change, and coordinator-change counters. CrowCast also publishes the same operational signals through the `Elysium.CrowCast` `System.Diagnostics.Metrics` meter and uses `Microsoft.Extensions.Logging`.

Rejected messages, rate-limited messages, and rejected membership updates are reported separately. Rejection logs are sampled after the first occurrence so hostile input cannot create an unbounded logging workload.

## Input protection

CrowCast validates both locally published and remotely received data. The defaults limit application payloads to 64 KiB, metadata to 32 entries, future timestamps to 30 seconds of clock skew, and inbound delivery to 10,000 messages per second per replica. These values are configurable through `CrowCastOptions`.

The Akka backend uses only the dedicated CrowCast serializer for its wire contracts, disables fallback serialization for unregistered types, rejects unknown JSON fields, and caps remote frames at 256 KiB. Increase `MaximumFrameSizeBytes` deliberately if application limits are raised; gossip is intended for compact notifications rather than bulk data transfer.

## Example

Run two local replicas in separate terminals:

```powershell
dotnet run --project samples/CrowCast.Sample -- --port 4053 --seed-port 4053
dotnet run --project samples/CrowCast.Sample -- --port 4054 --seed-port 4053
```

Both replicas generate their identities at runtime, discover each other, exchange sample messages, and report the same coordinator after membership converges.

To exercise DNS discovery instead of a fixed local seed, pass `--seed-service <dns-name>` to every replica.

## Security status

CrowCast does not require Kubernetes or manage certificates. Transport authentication, authorization, and encryption remain the responsibility of the deployment environment and are deliberately deferred from the core library. Input limits, strict serialization, bounded deduplication, clock-skew rejection, and rate limiting reduce accidental or hostile resource consumption, but they do not authenticate a replica.

Keep the Akka port inside a trusted network boundary. For Kubernetes, start with the optional [`NetworkPolicy`](deploy/kubernetes/network-policy.yaml): replace the application label and port so only replicas of the same workload in the namespace can reach CrowCast. Selecting a pod with an ingress policy isolates all its ingress, so add separate allow policies for the application's HTTP, metrics, and health-check traffic. Network policies are additive; another policy that permits broad ingress can also reopen the CrowCast port. If the platform already supplies workload mTLS through a service mesh or CNI, CrowCast can use that transport without owning certificates.

`AdditionalConfiguration` can override advanced Akka settings and should therefore be controlled as security-sensitive application configuration.

See [architecture](docs/architecture.md) for protocol boundaries, extension points, and delivery semantics.

## Build and package

```powershell
dotnet restore CrowCast.sln
dotnet build CrowCast.sln --configuration Release
dotnet test CrowCast.sln --configuration Release
dotnet pack CrowCast.sln --configuration Release --output nupkgs
```

To release, add a non-empty `## [vX.Y.Z]` section to `CHANGELOG.md`, place the same tag in `.release`, and push to `main`. The repository must define `NUGET_USER` and a nuget.org Trusted Publishing policy.
