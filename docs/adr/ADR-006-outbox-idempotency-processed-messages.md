# ADR-006: Outbox-style idempotency via ProcessedMessages table

## Status
Accepted — Implemented

## Context
Message brokers deliver at-least-once. A redelivered message processed without deduplication causes duplicate bids — a correctness defect in a financial context.

## Decision
Every consumed message is checked against a `processed_messages` table before processing. A unique index on `message_id` prevents duplicates. The idempotency record is inserted in the **same DB transaction** as the business operation.

```sql
-- same transaction:
INSERT INTO processed_messages (message_id, processed_at_utc) VALUES (@id, NOW());
INSERT INTO bids (...) VALUES (...);
```

If a redelivered message arrives, the `processed_messages` INSERT fails the unique constraint and the transaction is rolled back without re-processing the bid.

**Rejected**: In-memory deduplication (lost on restart); relying on RabbitMQ at-most-once (not guaranteed).

## Consequences
- (+) Exactly-once processing across service restarts and consumer crashes
- (+) Prevents duplicate bids from replayed messages
- (+) Deduplication record survives process restarts (DB-backed)
- (-) `processed_messages` table grows unboundedly — requires a periodic purge job (e.g. rows older than 30 days)
- (-) Adds one INSERT per consumed message on the hot path
