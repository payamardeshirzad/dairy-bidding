# ADR-039: Configurable retry policy for optimistic concurrency via IConfiguration

## Status
Proposed — Not yet implemented

## Context
The `OptimisticRetry` helper introduced alongside ADR-022 uses hardcoded defaults:

- `maxAttempts = 5`
- `baseDelayMs = 50` (exponential jitter ceiling per attempt: `Random.Shared.Next(0, baseDelayMs * 2^attempt)`)

These values were chosen as reasonable starting points at low-to-moderate load. Under high load — flash-sale dairy lots, end-of-auction sniping bursts — two problems emerge:

1. **Under-configured**: 5 attempts at 50 ms base may exhaust before the contending writer has committed, causing unnecessary propagation to MassTransit retry → eventually DLQ.
2. **Over-configured**: Under low load, the 5-attempt ceiling introduces unnecessary latency for a process that would have succeeded on attempt 2.

Hardcoded values cannot be tuned without a code change and redeployment. Load test evidence is required before optimal values are known.

## Decision
Defer making `OptimisticRetry` configuration-aware until load test data is available.

Once load tests have been run and optimal values identified, expose the policy as named options:

```json
"OptimisticRetry": {
  "BidReadModel":    { "MaxAttempts": 8, "BaseDelayMs": 30 },
  "AuctionCounters": { "MaxAttempts": 5, "BaseDelayMs": 50 }
}
```

Bind via `IOptions<OptimisticRetryOptions>` per use case. Named policies are preferred over a single global policy so each use case can be tuned independently.

**Until this ADR is implemented**, `OptimisticRetry` uses public constants (`DefaultMaxAttempts`, `DefaultBaseDelayMs`) referenced explicitly by name at each call site:

```csharp
await OptimisticRetry.ExecuteAsync(
    maxAttempts: OptimisticRetry.DefaultMaxAttempts,
    baseDelayMs: OptimisticRetry.DefaultBaseDelayMs,
    ...);
```

This makes the defaults visible and easy to locate when wiring up configuration.

**Trigger for implementation**: load test shows either (a) retry exhaustion rate > 0.1% of bid events, or (b) p99 bid acceptance latency > 500 ms attributable to retry delay.

**Rejected**: Configuring from the start without load test data (premature optimisation — adds `IOptions` plumbing with no validated benefit).

## Consequences
- (+) No premature abstraction; `OptimisticRetry` stays a simple static helper until evidence demands more
- (+) Call sites document retry parameters via named constants — visible and searchable
- (-) Requires code change + redeployment to tune retry behaviour before this ADR is implemented
- (-) `BiddingService` and `AuctionService` call sites could silently use different values if they diverge — mitigated by the shared public constants on `OptimisticRetry`
