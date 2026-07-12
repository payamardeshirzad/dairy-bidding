# ADR-023: No cross-database foreign keys; use reference UUIDs

## Status
Accepted — Implemented

## Context
Each service owns its database. An AuctionService table cannot have a PostgreSQL `FOREIGN KEY` constraint referencing `identity_db.users` — they are separate connection strings and potentially separate PostgreSQL instances.

## Decision
Cross-service references are stored as plain `UUID` columns with no FK constraint. Example: `auctions.seller_id` references a user that lives in `identity_db`.

Referential integrity across service boundaries is maintained by:
- The event-driven consistency model (events confirm referenced entities exist before the row is created)
- Saga compensation flows (future) to handle orphaned references

**Rejected**: Cross-database foreign keys (PostgreSQL does not support them); duplicating referenced data as full copies (data ownership becomes unclear).

## Consequences
- (+) Services are independently deployable and scalable — no distributed FK constraint failures
- (+) Schema changes in one service do not break others
- (-) Orphaned references are possible if services become inconsistent — requires compensating events or saga coordination
- (-) No cascade delete across services — must be handled explicitly per domain
