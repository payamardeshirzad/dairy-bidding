# ADR-022: Optimistic locking via row_version on contested tables

## Status
Accepted — Not yet implemented

## Context
The `auctions` table is updated by multiple concurrent processes: bid acceptance, anti-snipe extension (see ADR-034), and status transitions. A last-write-wins strategy silently discards updates (e.g. two bid acceptances could overwrite `current_price` to a stale value).

## Decision
Contested tables carry a `row_version INT DEFAULT 0` column. All `UPDATE` statements include `WHERE row_version = <expected>` and increment `row_version`. A zero-rows-updated result signals a concurrent modification — the caller retries.

```sql
UPDATE auctions
SET current_price = @newPrice, row_version = row_version + 1
WHERE id = @id AND row_version = @expectedVersion;
-- 0 rows updated → concurrent modification → retry
```

**Rejected**: Pessimistic locking (`SELECT FOR UPDATE`) — blocks concurrent readers; advisory locks — require explicit release.

## Consequences
- (+) No pessimistic locks — high concurrency on the auction hot path
- (+) Lost update problem eliminated at DB level
- (-) Retry logic required in application layer for all contested writes
- (-) Long-running transactions increase retry frequency under high bid volume
- **Pending**: Requires implementation alongside `current_price` denormalization (ADR-028)
