# ADR-020: TIMESTAMPTZ for all timestamp columns

## Status
Accepted — Implemented

## Context
The platform has buyers and sellers across EU, UK, and US. Auctions close at precise moments. Storing timestamps without timezone (`TIMESTAMP`) causes silent data corruption when the server timezone changes or when records are compared across services running in different locales.

## Decision
Every timestamp column uses `TIMESTAMPTZ` (PostgreSQL timezone-aware timestamp). All application code stores and retrieves UTC.

In EF Core with Npgsql, `DateTime` properties map to `TIMESTAMPTZ` when `UseTimestampTzDateTimeKind = true` (default in Npgsql 8+). All `DateTime` values must be `DateTimeKind.Utc`.

**Rejected**: `TIMESTAMP` without timezone (silent corruption on server timezone changes); storing as Unix epoch `BIGINT` (no native date functions).

## Consequences
- (+) No timezone-related bugs; safe to move servers between regions
- (+) Correct ordering of events from multiple services regardless of server locale
- (+) PostgreSQL date functions work correctly on `TIMESTAMPTZ` columns
- (-) `DateTime.UtcNow` must be used consistently — `DateTime.Now` will produce incorrect UTC storage
