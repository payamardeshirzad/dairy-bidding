# ADR-019: DECIMAL(18,4) for all monetary fields

## Status
Accepted — Partially Implemented

## Context
IEEE 754 floating-point (`FLOAT`, `DOUBLE`) cannot represent most decimal fractions exactly. `0.1 + 0.2 ≠ 0.3` in binary float arithmetic. On a platform processing financial transactions, rounding errors compound into real discrepancies.

## Decision
Every monetary column uses `DECIMAL(18,4)`. This applies to: `amount`, `reserve_price`, `starting_price`, `current_price`, `platform_fee`, `seller_payout`, and all equivalent columns across all services.

In EF Core:

```csharp
modelBuilder.Entity<Bid>()
    .Property(b => b.Amount)
    .HasPrecision(18, 4);
```

**Rejected**: `FLOAT` / `DOUBLE` (binary representation errors); `DECIMAL(18,2)` (insufficient precision for per-unit pricing calculations).

## Consequences
- (+) Exact decimal arithmetic — no rounding errors on monetary values
- (+) Matches Stripe's API representation (exact decimals)
- (-) Slightly more storage than `FLOAT`; not significant at this scale
- **Implementation status**: BiddingService and AuctionService use `decimal` in C# models; `DECIMAL(18,4)` precision enforced via EF Core fluent API
