# ADR-024: Client-generated idempotency key with DB UNIQUE constraint on bids

## Status
Accepted — Implemented

## Context
A bidder's network drops after sending a bid but before receiving the server's acknowledgement. The client retries. Without deduplication, the same bid is accepted twice at different processing times, corrupting the auction state.

## Decision
The client generates a `UUID` idempotency key before sending the bid request. The `bids` table has a `UNIQUE(idempotency_key)` constraint. If a retry arrives, the `INSERT` fails the unique constraint and the server returns the original bid result.

This is the **final safety net** even if an upstream cache (Redis) has expired or missed the key.

```http
POST /api/bids
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
```

**Rejected**: Server-generated idempotency keys (client cannot retry safely without the key); in-memory deduplication (lost on restart).

## Consequences
- (+) Exactly-once bid placement regardless of network retries
- (+) Constraint lives at DB level — cannot be bypassed by application logic bugs
- (+) Works across service restarts (DB-backed, not in-memory)
- (-) Client must generate and persist the UUID before sending the request
- (-) The `bids` table requires a unique index on `idempotency_key`, adding index maintenance overhead
