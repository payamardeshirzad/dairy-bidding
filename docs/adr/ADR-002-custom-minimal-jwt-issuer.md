# ADR-002: Custom minimal JWT issuer over Duende IdentityServer (initial implementation)

## Status
Accepted — Superseded by ADR-036

## Context
IdentityService needs to issue tokens that downstream services can validate. A full OIDC server was considered but the initial slice is an internal tool with a small, known user base. OAuth2/OIDC flows were not required at this stage.

## Decision
IdentityService is a bespoke ASP.NET Core 9 Minimal API issuing HMAC-SHA256 JWTs. No Duende/OpenIddict.

- Signing key stored via `dotnet user-secrets` as `Jwt:SigningKey` (see ADR-003)
- `POST /auth/token` accepts `{username, password}`, validates against the local user store, returns a signed JWT
- Downstream services validate via `AddJwtBearer` with a shared `IssuerSigningKey`

**Rejected**: Duende IdentityServer (commercial licence beyond 5 users in production), Microsoft Identity Platform (cloud dependency), OpenIddict (more ceremony than needed at this stage).

## Consequences
- (+) Zero external service dependency — entire auth stack runs in-process
- (+) No licence cost at the initial phase
- (-) Shared symmetric key: every service holds the secret — key rotation requires simultaneous redeployment of all services
- (-) No OIDC discovery, no JWKS endpoint, no refresh tokens, no B2B federation
- (-) XSS token theft risk accepted (see ADR-012)
- **This decision was explicitly marked as a stepping stone; superseded by ADR-036**
