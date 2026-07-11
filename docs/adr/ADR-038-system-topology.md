# ADR-038: System topology — service decomposition, communication patterns, and exchange topology

## Status
Accepted — Partially Implemented (core services running; CatalogService, PaymentService, LedgerService, NotificationService planned)

## Context
The full platform requires more services than the current MVP. The API Gateway, exchange topology, and client communication patterns need a documented target architecture so future services are designed consistently.

## Decision

### Three-tier topology

```
Client Layer → API Gateway → Services → Event Bus → Downstream Processors
```

**Client types:**
- **Web** (React SPA): HTTPS + WebSocket (SignalR)
- **Mobile**: HTTPS + WebSocket (SignalR)
- **B2B**: REST and/or gRPC

### Services (current + planned)

| Service | Port | Status |
|---|---|---|
| IdentityService | 5245 | Implemented |
| ApiGateway (YARP) | 5000 | Implemented |
| AuctionService | 5255 | Implemented |
| BiddingService | 5170 | Implemented |
| CatalogService | TBD | Planned |
| PaymentService | TBD | Planned |
| LedgerService | TBD | Planned |
| NotificationService | TBD | Planned |

**Production-grade API Gateway**: Kong considered as a replacement for YARP when non-.NET services are added (see ADR-010).

### Exchange topology (RabbitMQ)

| Exchange | Type | Purpose |
|---|---|---|
| `bids.topic` | Topic | Per-auction bid routing (key: `auction.{id}`) — preserves order per auction |
| `auctions.fanout` | Fanout | All auction lifecycle events → all interested consumers |
| `notifications.direct` | Direct | Per-user notification delivery (key: `user.{id}`) |

**All queues are Quorum Queues** (ADR-037 discipline #2).

### Communication patterns

| Pattern | Transport | Use case |
|---|---|---|
| Synchronous request/response | HTTPS (YARP) | UI reads, authentication, bid submission |
| Async events | RabbitMQ + MassTransit | Bid accepted → update auction price, trigger notification |
| Real-time push | SignalR (WebSocket) | Live bid updates, auction close countdown |
| Scheduled jobs | Quartz.NET | Auction close evaluation, outbox sweep, payment retry |

## Consequences
- (+) All future services have a documented template to follow
- (+) Exchange topology enforces per-auction ordering without application-layer coordination
- (+) Separation of sync vs async vs real-time paths keeps each service focused
- (-) Full topology is partially implemented — CatalogService, PaymentService, LedgerService, NotificationService each require a delivery sprint
- (-) SignalR Redis backplane (ADR-016) must be activated before NotificationService scales horizontally
