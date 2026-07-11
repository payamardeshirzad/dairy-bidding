# ADR-017: UUID primary keys for all entity tables

## Status
Accepted — Partially Implemented

## Context
Sequential integer PKs (SERIAL/BIGSERIAL) leak business information (e.g. user count, bid volume) and are guessable in URLs, enabling enumeration attacks (OWASP A01 — Broken Access Control).

## Decision
All entity tables use `UUID` as primary key. UUIDs are generated server-side (not DB `SERIAL`) so they can be assigned in the application layer before the `INSERT`.

```sql
id UUID PRIMARY KEY DEFAULT gen_random_uuid()
```

**Exception**: `payment_audit_log.id` uses `BIGSERIAL` — see ADR-018.

**Rejected**: Sequential integer PKs (guessable, leak row count).

## Consequences
- (+) No enumeration attacks via predictable IDs in URLs
- (+) IDs can be generated in the application layer before the DB round-trip
- (-) Slightly larger index size vs `BIGINT`
- (-) Random UUID v4 inserts cause B-tree page splits — mitigate with UUID v7 (time-ordered) when upgrading PostgreSQL 17+
- **Implementation status**: BiddingService and AuctionService use UUIDs; remaining services to be aligned when implemented
