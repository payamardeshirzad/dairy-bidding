# ADR-008: Shared Contracts project for cross-service events

## Status
Accepted — Implemented

## Context
When ServiceA publishes an event and ServiceB consumes it, both need to reference the same event class. Duplicating event types per service causes definitions to diverge silently over time, producing deserialization failures that are hard to diagnose.

## Decision
All integration event message contracts are defined in **`DairyBidding.Contracts`** (`src/DairyBidding.Contracts/`) as C# `record` types. Both publisher and consumer services add a `<ProjectReference>` to this project.

Current contracts:

| Event | Description |
|---|---|
| `BidPlacedEvent` | Published by BiddingService when a bid is accepted |
| `AuctionStatusChangedEvent` | Published by AuctionService when auction status changes |

Contracts must be **backward compatible** — add new optional properties rather than renaming or removing existing ones.

**Rejected**: Duplicating event types per service; JSON schema files; NuGet package (adds publishing overhead for a monorepo).

## Consequences
- (+) Single source of truth for event schemas — compile-time safety across service boundaries
- (+) Any schema mismatch produces a build error rather than a runtime deserialization failure
- (-) All services that publish or consume events must reference this project
- (-) In a future multi-repo setup, this project should be extracted to a versioned NuGet package
