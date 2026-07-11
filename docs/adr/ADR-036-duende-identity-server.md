# ADR-036: Adopt Duende IdentityServer to replace the custom JWT issuer

## Status
Accepted — Implemented (supersedes ADR-002)

## Context
The custom JWT issuer (ADR-002) is constrained to HMAC-SHA256 tokens with a shared secret. Scaling to B2B buyers requires SAML/OIDC federation. The BFF pattern requires token exchange (RFC 8693). JWKS-based asymmetric validation is needed so downstream services never hold a signing secret. `IEventSink` integration is required for OpenTelemetry token event tracing.

Commercial licence threshold: free for up to 5 production users; Duende Community licence applies beyond that. This is acceptable for a platform at this stage.

## Decision
Replace the custom JWT issuer with **Duende IdentityServer 7.2.0** in `DairyBidding.IdentityService`.

Key changes:
- RS256 asymmetric signing key (auto-generated on first start; persisted to `Data/IdentityServerKeys/`)
- Downstream services validate via `options.Authority` → JWKS discovery; they no longer hold any signing secret
- In-memory configuration for development (`AddInMemoryClients`, `AddInMemoryApiScopes`)
- `IEventSink` registered for OpenTelemetry event tracing
- `POST /connect/token` (client credentials + resource owner password flows) replaces `POST /auth/token`
- Exposes `/.well-known/openid-configuration` and `/connect/jwks` proxied through YARP (ADR-010)

**Rejected**: OpenIddict (more ceremony, fewer commercial-grade features), Azure AD (cloud lock-in), KeyCloak (JVM operational overhead), keeping the custom issuer (cannot support federation or BFF).

## Consequences
- (+) JWKS-based validation: downstream services never hold a signing secret
- (+) Token Exchange (RFC 8693) available for BFF pattern
- (+) B2B SAML/OIDC federation capability unlocked
- (+) `IEventSink` → OpenTelemetry — token events fully traced
- (+) XSS risk from ADR-012 formally closed: BFF pattern moves tokens to `httpOnly` session cookies
- (-) Duende commercial licence required above 5 production users
- (-) In-cluster RSA key management requires a key persistence strategy (currently file-based)
