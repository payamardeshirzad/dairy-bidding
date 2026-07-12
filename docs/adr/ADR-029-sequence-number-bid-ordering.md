# ADR-029: Monotonic sequence_number per auction for deterministic bid ordering

## Status
Accepted — Not yet implemented

## Context
Two bids can arrive at the server within the same millisecond. `placed_at TIMESTAMPTZ` alone cannot determine which bid was first. Ties resolved arbitrarily create disputable outcomes in a legally significant auction context.

## Decision
`bids.sequence_number` is a `BIGINT` populated from a PostgreSQL sequence scoped per `auction_id`:

```sql
sequence_number BIGINT NOT NULL DEFAULT nextval('bids_sequence_number_seq')
```

This gives every bid within an auction an unambiguous ordinal position regardless of clock precision.

**Rejected**: Timestamp-based ordering alone (millisecond collisions possible); application-layer sequence counters (race conditions under concurrent writes); global sequence (does not scale horizontally).

## Consequences
- (+) Deterministic, unambiguous bid ordering — defensible in dispute resolution
- (+) Immune to clock skew across application server instances
- (-) Requires a DB-level sequence call per bid (either a pre-INSERT `nextval` or same-transaction assignment)
- (-) Per-auction sequences add schema complexity; a single global sequence per table is simpler but creates a hot-spot under high bid volume
