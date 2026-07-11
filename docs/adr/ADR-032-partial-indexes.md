# ADR-032: Partial indexes on high-frequency large tables

## Status
Accepted — Not yet implemented

## Context
Full table scans on `outbox_messages` (polling for pending rows) and `auctions` (listing active auctions) become expensive as those tables grow. A full index on `outbox_messages.status` scans all rows including `sent`/`failed` rows that are never polled again.

## Decision
Partial indexes covering only the relevant subset of rows:

```sql
-- OutboxRelayWorker poll query
CREATE INDEX idx_outbox_pending
  ON outbox_messages (locked_until)
  WHERE status = 'pending' AND locked_until < NOW();

-- Active auction listing (hot read path)
CREATE INDEX idx_auctions_active
  ON auctions (ends_at)
  WHERE status IN ('live', 'closing');
```

**Rejected**: Full indexes on `status` columns (include all historical rows — grow indefinitely).

## Consequences
- (+) Index size bounded to active rows only — stays small even as historical data accumulates
- (+) Worker poll query performance independent of historical outbox volume
- (+) Active auction listing O(active_count) not O(total_auctions)
- (-) Partial indexes are PostgreSQL-specific; not portable to other databases
- (-) Index requires maintenance when `status` values change (rows leaving the partial index predicate are removed automatically by PostgreSQL)
