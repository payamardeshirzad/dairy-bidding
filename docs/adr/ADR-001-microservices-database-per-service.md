# ADR-001: Microservices with database-per-service

## Status
Accepted — Implemented

## Context
The platform covers distinct business domains: identity, catalogue, auctions, bidding, payments, and notifications. A monolith would couple these domains, preventing independent deployments and making it impossible to scale hot paths (bid acceptance) independently of cold paths (catalogue browsing).

## Decision
Split into independent services, each with its own PostgreSQL database:

| Service | Port | Database |
|---|---|---|
| IdentityService | 5245 | `identity_db` |
| AuctionService | 5255 | `auction_db` |
| BiddingService | 5170 | `bidding_db` |
| ApiGateway | 5000 | — |

Future services (CatalogService, PaymentService, LedgerService, NotificationService) follow the same pattern.

**Rejected**: Monolith; shared database (any service can read/write any table).

## Consequences
- (+) Independent deployability and fault isolation — a crashed BiddingService does not affect the catalogue
- (+) Each service scaled independently based on load characteristics
- (+) Teams can own a service end-to-end without stepping on each other
- (-) Cross-service data consistency must be managed via events (eventual consistency), not DB transactions
- (-) Operational overhead: N services to deploy, monitor, and maintain instead of one
