# CrowCast architecture

## Design goals

CrowCast is designed primarily for ephemeral replicas of the same service. A replica has no configured node name: `CrowCastNodeIdentity.New()` creates a random node ID and random generation for each runtime. `ClusterName` is a shared namespace used to form a cluster and is never treated as an instance identity.

The public contract stays independent of Akka, Kubernetes, databases, brokers, and application models.

## Layers

`CrowCast` owns:

- public membership and message models;
- `ICrowCast`, `ICrowCastBackend`, and `ICrowCastNodeDiscovery`;
- lifecycle integration with `IHostedService`;
- bounded in-memory deduplication and message expiration;
- configurable payload, metadata, clock-skew, and receive-rate limits;
- event-handler dispatch;
- deterministic coordinator calculation;
- metrics, logging, and diagnostic snapshots.

`CrowCast.Akka` owns:

- Akka ActorSystem and Cluster lifecycle;
- seed conversion from the discovery abstraction;
- reachability and membership-event mapping;
- periodic node announcements;
- Distributed PubSub gossip dissemination;
- versioned wire messages and a dedicated serializer.

A replacement backend implements `ICrowCastBackend`, receives an immutable `CrowCastBackendContext`, and emits backend notifications. Consumer applications remain attached to `ICrowCast` and `ICrowCastEventHandler`.

## Membership flow

1. The core creates a random identity and starts the selected backend.
2. Discovery returns contact endpoints. With no contacts, the Akka backend bootstraps itself; otherwise it joins the configured seeds.
3. Every replica periodically publishes an announcement containing its identity, generation, endpoint, start time, and metadata.
4. Akka reachability events map to `Alive` or `Suspect`; downing/removal maps to `Dead`.
5. A CrowCast departure announcement maps to terminal `Left` before transport shutdown.
6. An announcement from the same generation can refute `Suspect`. Terminal states are not resurrected; a restarted replica has a new identity and generation.

Akka's phi-accrual failure detector tolerates variable network latency. The split-brain resolver uses `keep-majority`, waits for the configured stable period, and does not use an external lease. This is suitable for advisory CrowCast coordination but is not a source of strong consistency.

## Message flow

1. The consumer creates a globally unique message ID, type, opaque bytes, and optional metadata.
2. The core attaches the local identity as origin when none is supplied.
3. The backend publishes the versioned envelope to the cluster topic.
4. Each receiving core rejects expired messages and suppresses an ID already present in its bounded in-memory deduplication window.
5. Malformed, oversized, future-dated, or rate-limited input is rejected before application dispatch.
6. Accepted messages are delivered to every registered event handler.

Distributed PubSub is eventually consistent and is not durable. Messages published while no subscriber is reachable can be lost. Delivery order can differ between nodes, and a message can reappear after deduplication state expires or a process restarts.

## Coordination

The coordinator is the lowest `(NodeId, Generation)` pair among locally `Alive` members. The choice is deterministic for replicas that see the same membership, requires no fixed node names, and changes automatically as membership changes.

It deliberately does not use Cluster Singleton, a lease, or a database lock. Two partitions can temporarily expose different coordinators; application actions triggered by coordination must tolerate duplicates and retries.

## Operational signals

The `Elysium.CrowCast` meter exposes:

- `crowcast.members`, tagged by status;
- `crowcast.messages.published`;
- `crowcast.messages.received`;
- `crowcast.messages.duplicates`;
- `crowcast.messages.expired`;
- `crowcast.messages.rejected`, tagged by rejection reason;
- `crowcast.membership.rejected`, tagged by rejection reason;
- `crowcast.membership.changes`;
- `crowcast.coordinator.changes`.

No telemetry exporter is required or selected by CrowCast.

## Security boundary

The Akka adapter registers a dedicated, versioned serializer for its three wire contracts and disables fallback serialization of unregistered types. Remote frames, message bodies, metadata, timestamps, deduplication state, and receive rate are bounded. These controls protect parser and process resources; they do not establish the identity or honesty of a replica.

CrowCast remains independent of Kubernetes and certificate infrastructure. A deployment must restrict the gossip port to the intended replicas, for example with the provided namespace-scoped `NetworkPolicy`. Environments that require authenticated or encrypted pod-to-pod transport should provide it through their existing service mesh, CNI, or an optional future transport-security integration. CrowCast will not operate a certificate authority or place a shared CA private key in replicas.
