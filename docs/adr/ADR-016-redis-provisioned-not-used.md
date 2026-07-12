# ADR-016: Redis provisioned but not yet used

## Status
Accepted — Provisioned, not yet connected

## Context
Three distinct use cases for Redis are planned (see ADR-037 and ADR-038):

1. **Atomic `current_price` update** (BiddingService) — Lua compare-and-set to avoid a DB round-trip on every bid
2. **SignalR Redis backplane** (NotificationService) — required for horizontal scaling of real-time bid updates
3. **Active auction read cache** — short-TTL cache for `GET /auctions/{id}` under load

Starting Redis integration before the use case is built would add cache invalidation complexity with no observable benefit.

## Decision
**Redis 7** is defined in `compose.yml` to establish the correct network topology. No application service references it yet.

```yaml
redis:
  image: docker.io/redis:7-alpine
  ports: ["6379:6379"]
```

**Rejected**: Removing Redis entirely (would require re-adding it and updating network config later).

## Consequences
- (+) Network topology is correct — services can connect to Redis when ready without Compose changes
- (+) Container is waiting with zero code changes needed when BiddingService adds Lua scripts
- (-) Redis adds ~50 MB to the local stack memory footprint without current benefit
- **Activated in BiddingService during ADR-037 MassTransit migration for atomic bid operations**
