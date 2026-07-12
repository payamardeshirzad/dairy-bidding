# ADR-010: YARP as API Gateway

## Status
Accepted — Implemented

## Context
All client traffic should route through a single stable ingress rather than hitting individual service ports. An API Gateway centralises TLS termination, JWT validation, rate limiting, CORS, and routing without duplicating these concerns in every service.

## Decision
`DairyBidding.ApiGateway` is an ASP.NET Core 9 project using **YARP 2.3.0** as a reverse proxy.

Routes defined in `appsettings.Development.json`:

| Route prefix | Destination cluster |
|---|---|
| `/api/auctions/{**catch-all}` | AuctionService (:5255) |
| `/api/bids/{**catch-all}` | BiddingService (:5170) |
| `/connect/{**catch-all}` | IdentityService (:5245) |
| `/.well-known/{**catch-all}` | IdentityService (:5245) |

The gateway runs on port `5000` and is the only entry point exposed to clients.

**Rejected**: Nginx (separate container, no .NET middleware reuse), Ocelot (less actively maintained), Envoy (non-.NET, separate config language).

## Consequences
- (+) YARP runs in-process with .NET — ASP.NET Core middleware (auth, rate-limiting, CORS) applies without additional plugins
- (+) Routes are configurable in `appsettings.json` without code changes
- (+) OIDC discovery and token endpoints proxied — frontend uses a single base URL
- (-) YARP is .NET-only; if non-.NET services are added later, Kong or Envoy may be more appropriate (ADR-038 notes Kong as the production-grade alternative)
