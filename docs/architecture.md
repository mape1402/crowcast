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

`CrowCast.Observability` owns:

- a bounded, in-memory projection of the local replica's membership view;
- redaction of endpoints and metadata unless explicitly included;
- activity events and rolling metric samples for dashboards and reporters;
- no transport, exporter, database, or UI dependency.

`CrowCast.WebUI` and `CrowCast.WebUI.Shell` own the optional embedded dashboard. The shell uses the Elysium ZoOrce layout, AG Grid for operational tables, Apache ECharts for trends and topology, and SignalR only as an invalidation signal. Browser clients fetch fresh data from Razor Page handlers after an invalidation; the hub does not carry authoritative cluster state.

`CrowCast.ControlPlane.Protocol` defines the versioned gRPC wire contract. `CrowCast.ControlPlane.Client` reports a replica's local view through one bidirectional HTTP/2 stream. `CrowCast.ControlPlane` ingests and aggregates those independent observer views, and `CrowCast.ControlPlane.WebUI` renders fleet, cluster, and observer-agreement views.

A replacement backend implements `ICrowCastBackend`, receives an immutable `CrowCastBackendContext`, and emits backend notifications. Consumer applications remain attached to `ICrowCast` and typed `ICrowCastEventHandler<TEvent>` contracts.

## Membership flow

1. The core creates a random identity and starts the selected backend.
2. Discovery returns contact endpoints. Static discovery is useful locally; DNS discovery resolves a stable service name into the current ephemeral replica addresses. With no contacts, the Akka backend bootstraps itself; otherwise it joins the discovered seeds.
3. Every replica periodically publishes an announcement containing its identity, generation, endpoint, start time, and metadata.
4. Akka reachability events map to `Alive` or `Suspect`; downing/removal maps to `Dead`.
5. A CrowCast departure announcement maps to terminal `Left` before transport shutdown.
6. An announcement from the same generation can refute `Suspect`. Terminal states are not resurrected; a restarted replica has a new identity and generation.
7. Backend startup completes when Akka reports the local member as `Up`. `MemberRemoved` compacts the actor's endpoint and announcement state while a bounded terminal tombstone prevents delayed announcements from resurrecting that generation.

The core keeps at most `MaximumRetainedTerminalMembers` terminal records, ordered by their last change. This bounds memory during long-running Kubernetes scale and replacement cycles while preserving recent `Dead` and `Left` diagnostics.

Akka's phi-accrual failure detector tolerates variable network latency. The split-brain resolver uses `keep-majority`, waits for the configured stable period, and does not use an external lease. This is suitable for advisory CrowCast coordination but is not a source of strong consistency.

## Message flow

1. The consumer publishes a registered application payload. The core resolves its stable wire type, serializes it through `ICrowCastMessageSerializer`, and creates a globally unique envelope with optional metadata.
2. The core validates the envelope and attaches the local identity as origin. Advanced consumers can still provide a raw byte envelope directly.
3. The consumer selects all members, one exact execution identity, or the current advisory coordinator as the destination. The core resolves coordinator delivery to the identity visible in its current local view.
4. The Akka backend publishes the unchanged versioned envelope to either the shared cluster topic or a private topic derived from the destination `(NodeId, Generation)` pair.
5. Each receiving core rejects expired messages and suppresses an ID already present in its bounded in-memory deduplication window.
6. Malformed, oversized, future-dated, or rate-limited input is rejected before application dispatch.
7. Accepted messages are available to raw `ICrowCastEventHandler<CrowCastMessageReceived>` observers and are deserialized for registered `ICrowCastMessageHandler<TMessage>` implementations.

System.Text.Json with web defaults is the built-in application payload serializer. Consumers can replace it without changing the core, backend, targeting, or handler contracts. Wire message types are stable explicit registrations; CrowCast does not transmit CLR type names or dynamically load application assemblies.

Distributed PubSub is eventually consistent and is not durable. Messages published while no subscriber is reachable can be lost. Delivery order can differ between nodes, and a message can reappear after deduplication state expires or a process restarts. A member target identifies one execution generation, so a replacement pod does not inherit messages addressed to the previous process. A coordinator target is a snapshot of the publisher's local view, not a lease or routing guarantee across a partition.

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

## Observation and control-plane flow

1. Each replica independently creates a bounded observation snapshot from `ICrowCast` and `ICrowCastDiagnostics`.
2. The optional embedded dashboard reads that local snapshot. It therefore states what one replica currently believes, not global truth.
3. The optional control-plane client opens a bidirectional gRPC stream and sends `Hello`, full `Snapshot`, incremental `EventBatch`, and `Heartbeat` frames.
4. The server acknowledges frames, tracks observer freshness, and can request a full resynchronization. A disconnected reporter retries with bounded exponential backoff and does not block gossip or application startup.
5. The in-memory store keeps multiple observer perspectives for each logical `(environment, scope, service, cluster)` key. It derives health from report freshness, coordinator agreement, and membership agreement.
6. The control-plane dashboard exposes the fleet projection, a cluster selector, and one operational dashboard per logical cluster. It creates a stable consensus membership projection from fresh observers for the member inventory and topology, sums their current counters for approximate message telemetry, and separately retains every observer view for the agreement matrix. Heartbeats update freshness without changing which observer is treated as the visual center of the cluster. Disagreement is preserved as diagnostic evidence rather than overwritten by a supposedly authoritative replica.

The control plane is deliberately not on the gossip data path. Losing it affects dashboards and centralized telemetry only; membership and application messages continue between replicas. Its default store is bounded and non-durable. A future store can implement `ICrowCastControlPlaneStore` without changing the gRPC protocol or dashboards.

### gRPC compatibility

The protocol package uses a `v1` protobuf namespace and additive field evolution. The first frame must be a supported `Hello`; frame sequence numbers must increase monotonically. Snapshots carry their cluster and observer identity so the server can reject cross-stream mismatches. Delivery is at-least-once at the reporting layer: event sequence numbers are scoped to an observer, and the store deduplicates repeated observer/event pairs.

Backpressure, rejection, and resynchronization are control frames rather than application commands. The reporter never allows control-plane traffic to execute application business logic.

## Security boundary

The Akka adapter registers a dedicated, versioned serializer for its three wire contracts and disables fallback serialization of unregistered types. Remote frames, message bodies, metadata, timestamps, deduplication state, and receive rate are bounded. These controls protect parser and process resources; they do not establish the identity or honesty of a replica.

CrowCast remains independent of Kubernetes and certificate infrastructure. A deployment must restrict the gossip port to the intended replicas, for example with the provided namespace-scoped `NetworkPolicy`. The embedded and control-plane dashboards, SignalR hubs, and gRPC observation endpoint are also unauthenticated in this phase and must stay behind the application's existing trusted ingress and network policies.

Environments that require authenticated or encrypted pod-to-pod transport should provide it through their existing service mesh, CNI, or a future transport-security integration. CrowCast will not operate a certificate authority or place a shared CA private key in replicas.
