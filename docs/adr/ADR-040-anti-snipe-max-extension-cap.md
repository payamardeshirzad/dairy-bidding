# ADR-040: Cap anti-snipe extensions at a configurable maximum per auction

## Status
Accepted — Implemented (2026-07-12)

## Context
ADR-034 introduced an append-only `auction_extensions` table and the
`AntiSnipeOptions.WindowMinutes` / `AntiSnipeOptions.ExtensionMinutes` configuration.
The mechanism has no upper bound on the number of times a single auction can be
extended. If buyers continuously place bids within the closing window, an auction can
remain in the extended-closing state indefinitely.

This creates two problems:

1. **Operational**: An auction that never closes blocks settlement workflows and ties up lots.
2. **Trust**: Sellers and the platform have no guarantee that a lot will close within a
   predictable window. A dishonest participant could exploit the anti-snipe rule to keep
   an auction open beyond the intended session.

Industry precedent establishes a hard cap:
- **Copart** (salvage vehicle auctions): 2-minute extensions, capped at 10 per lot.
- **PropertyGuru Auction** (real estate): up to 10 extensions of 5 minutes each —
  maximum additional time of 50 minutes.
- **BidSpotter / Proxibid** (industrial equipment): configurable cap, defaulting to 10.

The common thread is **10 extensions** as the standard ceiling, providing ~50 minutes
of total extension time with a 5-minute interval — enough to accommodate genuine
last-second competition while preventing indefinite stalling.

## Decision
Add `MaxExtensionsPerAuction` (integer) to `AntiSnipeOptions`. Default value: **10**.

```json
"AntiSnipe": {
  "WindowMinutes": 5,
  "ExtensionMinutes": 5,
  "MaxExtensionsPerAuction": 10
}
```

Track the running extension count as a denormalized column `extension_count` on the
`auctions` table, incremented atomically with each extension inside the same
`OptimisticRetry` transaction that writes the `AuctionExtension` row and updates
`ends_at`. When `extension_count` reaches `MaxExtensionsPerAuction`, the anti-snipe
block is skipped — the bid is still accepted and recorded, but `ends_at` is not
extended.

**Why a column on `auctions`, not a `COUNT(*)` query against `auction_extensions`?**
`extension_count` on `auctions` is protected by the same `row_version` optimistic
concurrency token (ADR-022). No additional query is needed inside the retry loop: after
`ReloadAsync`, both `ends_at` and `extension_count` are refreshed in a single entity
read. A `COUNT(*)` read would require a separate database round-trip and would not be
protected by the concurrency token.

**Rejected alternatives:**
- `COUNT(*)` from `auction_extensions` inside the retry lambda: correct but adds an
  extra DB read per retry, not protected by the optimistic lock on `auctions`.
- Cap by total elapsed extension time (e.g., max 50 additional minutes): more complex
  to evaluate; `ExtensionCount × ExtensionMinutes` conveys the same information with
  simpler arithmetic.
- No cap (pure anti-snipe with configurable duration only): exposes the platform to
  indefinite auction-open exploitation.

## Consequences
- (+) Auction close time is bounded: with defaults, the latest possible close is
  `original_ends_at + 10 × 5 min = original_ends_at + 50 min`
- (+) `extension_count` is available in `GET /auctions/{id}` — buyers can see how
  close the auction is to the cap
- (+) Configurable per deployment — operators can lower the cap for simple lots and
  raise it for high-value items
- (+) No extra DB read inside the retry loop — count is co-located with `row_version`
  and refreshed by `ReloadAsync`
- (-) Adds one column to `auctions`; requires a migration
- (-) `extension_count` duplicates information derivable from
  `COUNT(auction_extensions WHERE auction_id = X)` — must be kept in sync; acceptable
  given the two writes always occur in the same transaction
