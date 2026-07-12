# ADR-041: Propagate auction end-time extensions via AuctionStatusChangedEvent

## Status
Accepted — Implemented (2026-07-12)

## Context
ADR-034 introduced anti-snipe logic: when a bid arrives within the closing window,
`BidPlacedHandler` (AuctionService) extends `auction.ends_at` and writes an immutable
row to `auction_extensions`. The extension is committed to the AuctionService database,
but BiddingService maintains its own `auction_read_models` table — a local projection
of auction state populated by consuming `AuctionStatusChangedEvent` from the
`auctions.fanout` exchange.

After an anti-snipe extension, BiddingService's `auction_read_models.ends_at` remains
at the pre-extension value. BiddingService guards every incoming bid with:

```csharp
if (auctionState.Status != "Active" || auctionState.EndsAt < DateTime.UtcNow)
    return Results.BadRequest(...);
```

A buyer placing a bid in the extended window is rejected because BiddingService still
believes the auction closed at the original `ends_at`. The bid is lost without the
buyer receiving a meaningful error — the auction appears open in AuctionService but
closed in BiddingService.

## Decision
When `BidPlacedHandler` successfully commits an anti-snipe extension, it publishes an
`AuctionStatusChangedEvent` via the MassTransit EF Core outbox with the updated
`EndsAt`. BiddingService's existing `AuctionStatusChangedConsumer` →
`AuctionStatusChangedHandler` pipeline already performs a full upsert of
`AuctionReadModel` including `EndsAt` — no new consumers, contracts, or routing
changes are required.

**Publish position — after `OptimisticRetry`, not inside it.**

Publishing inside the `OptimisticRetry` lambda is unsafe. When
`DbUpdateConcurrencyException` is thrown, the database transaction is rolled back, but
the EF change tracker retains any `OutboxMessage` entities in `Added` state. On the
next retry, a second call to `publishEndpoint.Publish()` adds another `OutboxMessage`.
Both are committed on the succeeding `SaveChangesAsync`, delivering two events to
BiddingService. The first event carries a stale `EndsAt` derived from the pre-reload
auction state.

The correct approach: after `OptimisticRetry.ExecuteAsync` returns (i.e., the auction
extension is durably committed), publish the event in a separate `db.SaveChangesAsync()`
that commits only the outbox row:

```csharp
// OptimisticRetry has already committed: auction.EndsAt, auction.ExtensionCount,
// AuctionExtension row. pendingExtension is non-null iff an extension was applied.
if (pendingExtension is not null)
{
    await publishEndpoint.Publish(new AuctionStatusChangedEvent(
        Guid.NewGuid().ToString("N"),
        auction.Id, auction.Title,
        auction.Status.ToString(),
        auction.StartsAt,
        auction.EndsAt,          // ← post-extension value
        DateTime.UtcNow,
        auction.StartingPrice), ct);

    await db.SaveChangesAsync(ct); // commits outbox row only
}
```

**Known inconsistency window.** If the second `SaveChangesAsync` fails transiently and
MassTransit retries the `BidPlacedConsumer`, the `ProcessedMessages` guard fires an
early return — the outbox message is never published for this invocation.
BiddingService's `EndsAt` remains stale until the auction's next lifecycle event
(typically `Closed`), which publishes a fresh `AuctionStatusChangedEvent` with the
final `EndsAt`. For the dairy auction domain, bids placed in the extension window
during this failure window are rejected at BiddingService with an "auction closed"
error; they may re-bid once BiddingService's state is corrected. This is acceptable
given the rarity of the failure path and the correctness of the eventual state.

**Rejected alternatives:**

- *Publish inside `OptimisticRetry` with a `bool extensionEventQueued` guard:* The
  first attempt's `OutboxMessage` entity stays in the EF change tracker after rollback;
  the guard prevents a second `Publish()` call, so the committed outbox message carries
  the stale `EndsAt` from the first attempt's `auction.EndsAt` computation. Incorrect
  under high concurrency.

- *Clear `OutboxMessage` entities from the change tracker between retries:* Requires
  coupling application code to MassTransit internals (`OutboxMessage` type). Fragile
  across MassTransit version upgrades.

- *BiddingService queries AuctionService before rejecting a bid:* Synchronous
  service-to-service call on the hot bid path; introduces latency, coupling, and a
  failure mode if AuctionService is temporarily unreachable.

- *Periodic reconciliation job in BiddingService:* Corrects stale read models but adds
  operational complexity and does not close the inconsistency window for the live
  bidding path.

## Consequences
- (+) Existing event contract (`AuctionStatusChangedEvent`) and consumer
  (`AuctionStatusChangedHandler`) require no changes — zero new moving parts in
  BiddingService
- (+) BiddingService's `AuctionReadModel.EndsAt` is updated within milliseconds of each
  extension under normal operation
- (+) `auctions.fanout` exchange already routes to all interested services — additional
  subscribers benefit automatically
- (-) Two-phase commit between the extension row and the outbox message is not
  guaranteed: a crash between the two `SaveChangesAsync` calls leaves BiddingService
  stale until auction close
- (-) Adds a second `db.SaveChangesAsync()` call to `BidPlacedHandler` on extension
  paths; no impact on the common (no-extension) path
