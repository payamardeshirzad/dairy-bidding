# ADR-026: Distributed outbox lease via locked_until column

## Status
Accepted — Superseded by ADR-037 (MassTransit manages lease coordination)

## Context
Multiple instances of the outbox relay worker will run in Kubernetes. Without coordination, two workers pick up the same `outbox_messages` row and publish the same event twice to RabbitMQ.

## Decision
The `outbox_messages` table includes a `locked_until TIMESTAMPTZ` column. A worker claims a batch of rows via:

```sql
UPDATE outbox_messages
SET locked_until = NOW() + INTERVAL '30 seconds'
WHERE locked_until < NOW() OR locked_until IS NULL
RETURNING *;
```

Other workers skip claimed rows. If a worker crashes, the lease expires and another worker reclaims it.

**Rejected**: Redis-based distributed lock (adds Redis as a hard dependency for messaging); ZooKeeper (heavy operational overhead).

## Consequences
- (+) At-most-once publish attempt per worker at any instant
- (+) No external distributed lock system required
- (-) At-least-once delivery still possible if the worker publishes and crashes before marking `sent` — consumers must be idempotent (see ADR-006)
- (-) Lease timeout (30s) determines maximum extra delay after a worker crash
- **Superseded by ADR-037**: MassTransit's EF Core Outbox manages lease coordination internally using the same DB, replacing this hand-rolled mechanism.
