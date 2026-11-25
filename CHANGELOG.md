# Changelog

All notable changes to CrowCast will be documented in this file.

## [Unreleased]

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
