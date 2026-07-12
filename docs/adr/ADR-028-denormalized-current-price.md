# ADR-028: Denormalized current_price and counters on the auctions table

## Status
Accepted — Implemented

## Context
Every auction page load needs the current leading bid price and bid count. Computing these as `MAX(bids.amount)` and `COUNT(bids.id)` with a `JOIN` on every request is expensive under load, especially with thousands of concurrent auctions.

## Decision
`auctions.current_price`, `auctions.bid_count`, and `auctions.view_count` are denormalized columns updated atomically within the bid acceptance cycle (using optimistic locking from ADR-022):

```sql
UPDATE auctions
SET current_price = @newPrice,
    bid_count = bid_count + 1,
    row_version = row_version + 1
WHERE id = @id AND row_version = @expected;
```

**Rejected**: Aggregation query on every auction read (`MAX`/`COUNT` JOIN) — O(n) per request.

## Consequences
- (+) O(1) auction page reads — no aggregation query required
- (+) Reduces read load on the `bids` table
- (-) `current_price` can temporarily diverge from `MAX(bids.amount)` if a relay step fails — requires a periodic reconciliation job
- (-) Two sources of truth for bid count: `COUNT(bids.*)` must match `bid_count` in audits
- **Pending**: Requires AuctionService schema migration and BiddingService bid-acceptance update
