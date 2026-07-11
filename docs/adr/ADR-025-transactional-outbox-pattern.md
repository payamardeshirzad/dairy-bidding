# ADR-025: Transactional Outbox pattern for guaranteed event delivery

## Status
Accepted — Superseded by ADR-037 (MassTransit EF Core Outbox)

## Context
BiddingService must publish a `BidPlacedEvent` after accepting a bid. If the service publishes to RabbitMQ and then crashes before committing the DB transaction (or vice versa), the system ends up split-brain: a bid saved with no event published, or an event published for a rolled-back bid.

## Decision
Implement the Transactional Outbox pattern. When a bid is accepted, an `outbox_messages` row is written in the **same DB transaction** as the `bids` row. A background `OutboxRelayWorker` polls `outbox_messages WHERE status = 'pending'` and publishes to RabbitMQ, then marks rows `sent`.

```sql
-- single transaction:
INSERT INTO bids (...) VALUES (...);
INSERT INTO outbox_messages (payload, status) VALUES (@event, 'pending');
```

**Rejected**: Dual-write (direct `BasicPublish` after `SaveChanges`) — split-brain risk.

## Consequences
- (+) Atomicity between domain write and event publication — no lost events
- (+) Events survive process crashes (stored in DB before publish)
- (-) Adds latency between bid acceptance and event publication (polling interval)
- **Superseded by ADR-037**: MassTransit EF Core Outbox provides this pattern without hand-rolled plumbing. The hand-rolled `outbox_messages` table was replaced by MassTransit's `outbox_message` + `outbox_state` tables.
