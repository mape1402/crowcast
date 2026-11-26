# Changelog

All notable changes to CrowCast will be documented in this file.

## [Unreleased]

## [v1.0.0] - 2026-11-25

### Added

- Added the application-neutral CrowCast core package.
- Added random per-runtime node identities and generations.
- Added membership, event, message, deduplication, coordinator, metric, and diagnostic APIs.
- Added replaceable backend and node-discovery abstractions.
- Added the Akka.NET Cluster and Distributed PubSub backend.
- Added unit and real two-replica integration tests.
- Added a runnable multi-replica sample and architecture documentation.
- Added configurable payload, metadata, clock-skew, frame-size, and receive-rate limits.
- Added rejection metrics and sampled logs for invalid messages and membership updates.
- Hardened Akka with strict registered-type serialization and unknown-field rejection.
- Added an optional same-workload Kubernetes `NetworkPolicy` template.
- Kept transport authentication and encryption outside the core library and deployment-specific.
- Added bounded, redacted observation snapshots, activity events, and time-series samples.
- Added an optional embedded ZoOrce dashboard using AG Grid, Apache ECharts, and SignalR.
- Added a versioned bidirectional gRPC protocol and non-blocking replica reporter.
- Added an in-memory multi-cluster control plane with fleet health, observer agreement, and live updates.
- Added a ZoOrce control-plane dashboard and configurable multi-cluster demonstration.
- Added strongly typed event handlers and removed the need for consumer-side event switches.
- Added explicit delivery to all members, one execution identity, or the current advisory coordinator.
- Added registered typed payloads, automatic System.Text.Json serialization, typed message handlers, and a replaceable serializer abstraction.
