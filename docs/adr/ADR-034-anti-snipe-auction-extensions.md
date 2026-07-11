# ADR-034: Anti-snipe extension history in a dedicated auction_extensions table

## Status
Accepted — Not yet implemented

## Context
Anti-snipe logic extends an auction's end time when a bid is placed in the final minutes. Without a history of these extensions, a buyer who lost an auction can claim the closing time was moved unfairly, and there is no record to refute or confirm this.

## Decision
Every anti-snipe extension is recorded as an immutable row in an `auction_extensions` table:

```sql
CREATE TABLE auction_extensions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    auction_id UUID NOT NULL,
    bid_id UUID NOT NULL,           -- the bid that triggered the extension
    previous_end TIMESTAMPTZ NOT NULL,
    new_end TIMESTAMPTZ NOT NULL,
    extended_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

The table is append-only. No `UPDATE` or `DELETE` is ever issued against it.

**Rejected**: Overwriting `auctions.ends_at` with no history (no traceability for buyer disputes); storing extension count only on the `auctions` row (no individual event traceability).

## Consequences
- (+) Full traceability for buyer disputes — every extension linked to the specific bid that triggered it
- (+) Immutable audit log — cannot be retroactively altered
- (+) Extension history queryable per auction for regulatory and legal review
- (-) Each anti-snipe event writes an extra row — acceptable given extensions are low-frequency
