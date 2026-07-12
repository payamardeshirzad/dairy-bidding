# ADR-027: JSONB for event payloads in outbox and audit tables

## Status
Accepted — Not yet implemented

## Context
Outbox rows and audit log rows store serialized events. Using `TEXT` or `JSON` type makes the payload opaque — you cannot query inside it without deserializing in application code.

## Decision
Event payload columns use PostgreSQL `JSONB` type:

```sql
payload JSONB NOT NULL
```

This enables direct SQL queries such as:
```sql
WHERE payload->>'auction_id' = '550e8400-e29b-41d4-a716-446655440000'
```
for debugging, monitoring, and incident investigation without requiring application code.

**Rejected**: `TEXT` type (opaque — no SQL querying); `JSON` type (stored as-is, not parsed — no indexing possible).

## Consequences
- (+) Payload queryable and indexable at the DB level (GIN index on `JSONB` columns)
- (+) Easier operational debugging and ad-hoc investigation in pgAdmin or psql
- (-) Slightly higher storage overhead than `TEXT` (JSONB is parsed and stored as binary)
- (-) Schema changes to event types must be backward-compatible — no DB constraint enforces the event shape
- **Pending**: Applied when outbox and audit tables are added to remaining services
