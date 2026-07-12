# ADR-037: Adopt MassTransit, Polly, Quartz.NET, and Quorum Queues as the full messaging and resilience stack

## Status
Accepted — Implemented (supersedes ADR-005, ADR-025, ADR-026)

## Context
ADR-005 (raw RabbitMQ.Client) produced ~600 lines of hand-rolled publisher/consumer/retry/DLQ boilerplate with no transactional outbox and no saga support. ADR-025 and ADR-026 added an outbox and lease mechanism — further boilerplate that is now table-stakes for any production messaging stack.

## Decision
Replace the entire hand-rolled messaging layer with a production-grade stack:

| Concern | Library | Version |
|---|---|---|
| Messaging + EF Core Outbox | MassTransit | 8.3.6 |
| Resilience (retry, circuit-breaker, timeout) | Polly | 8.x |
| Scheduled jobs (auction close, outbox sweep) | Quartz.NET | 3.x |
| Cache + Lua scripts (atomic bid operations) | StackExchange.Redis | 2.x |
| Hot-path read projection | Dapper | 2.x |
| Queue durability | RabbitMQ Quorum Queues | — |

**Three non-negotiable disciplines:**
1. **Always use the EF Core Outbox** — `AddEntityFrameworkOutbox<TDbContext>()` on every service bus; no direct `Publish` without an outbox
2. **Always declare Quorum Queues** — via `EndpointConvention` and `UseQuorumQueue()` on all endpoint configurations
3. **Design the exchange topology for per-auction bid ordering** — Topic exchange `bids.topic` with routing key `auction.{auction_id}` ensures all bids for the same auction route to the same Quorum Queue and are processed in order

**Rejected**: Hand-rolled consumer/publisher boilerplate (ADR-005), NServiceBus (commercial licence per endpoint), Rebus (less active community).

## Consequences
- (+) MassTransit EF Core Outbox replaces hand-rolled ADR-025/ADR-026 outbox entirely
- (+) Saga support available for auction lifecycle workflows (close → payment → notification)
- (+) Polly pipelines replace ad-hoc retry loops across all services
- (+) Quorum Queues ensure no message loss during RabbitMQ node failover
- (-) MassTransit's EF Core Outbox adds `outbox_message` and `outbox_state` tables per service DB — migration required
- (-) Per-auction exchange topology adds design constraints on all future consumers of bid events
