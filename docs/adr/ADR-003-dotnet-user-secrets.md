# ADR-003: dotnet user-secrets for all sensitive configuration

## Status
Accepted — Implemented

## Context
Signing keys, database connection strings, and RabbitMQ credentials must not appear in source control. Hardcoded dev defaults in `appsettings.Development.json` are routinely committed accidentally and provide attackers with valid credentials when the repository is public or leaked.

## Decision
All secrets are stored exclusively via `dotnet user-secrets`. They never appear in `appsettings*.json` or any file tracked by git.

```
dotnet user-secrets set "Jwt:SigningKey" "<value>" --project src/DairyBidding.IdentityService
dotnet user-secrets set "ConnectionStrings:Postgres" "<value>" --project src/DairyBidding.BiddingService
dotnet user-secrets set "RabbitMQ:Password" "<value>" --project src/DairyBidding.BiddingService
```

`appsettings.json` contains only non-sensitive defaults (log levels, port numbers). `appsettings.Development.json` contains only non-sensitive local overrides.

**Rejected**: `.env` files (checked in by mistake too easily); hardcoded dev defaults in appsettings; Azure Key Vault (adds cloud dependency to local dev).

## Consequences
- (+) OWASP A02 (Cryptographic Failures) and A05 (Security Misconfiguration) addressed: secrets never committed to git
- (+) `dotnet user-secrets` is stored in the OS user profile, scoped to the project's `UserSecretsId`
- (-) Every developer must run `dotnet user-secrets set` commands after cloning — documented in `docs/local-setup.md`
- (-) CI/CD pipelines must inject secrets via environment variables or a vault, not user-secrets
