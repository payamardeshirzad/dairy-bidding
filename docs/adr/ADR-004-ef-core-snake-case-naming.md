# ADR-004: EF Core 9 code-first with snake_case naming convention

## Status
Accepted — Implemented

## Context
PostgreSQL's native convention is snake_case for table and column names. EF Core defaults to PascalCase, which requires double-quoting in raw SQL. Applying `HasColumnName()` to every property is verbose and error-prone.

## Decision
Use **EF Core 9** with the **Npgsql provider** and apply `UseSnakeCaseNamingConventions()` globally via `EFCore.NamingConventions 9.0.0`:

```csharp
options.UseNpgsql(connectionString)
       .UseSnakeCaseNamingConventions();
```

This is set once on `DbContextOptionsBuilder`, not per-entity or per-property.

Migrations are generated with `dotnet ef migrations add` and committed to source control under `Data/Migrations/`.

**Rejected**: Dapper (no migration tooling); raw SQL scripts (no POCO mapping); manual `HasColumnName()` annotations per property.

## Consequences
- (+) All table and column names in PostgreSQL are lowercase snake_case automatically
- (+) No double-quoting needed in raw SQL or Grafana queries
- (+) Single convention call rather than N `HasColumnName()` calls
- (-) Every new `DbContext` must include `.UseSnakeCaseNamingConventions()` — required in onboarding docs
- (-) Migrations generated before this convention was applied required rename migrations to fix column names
