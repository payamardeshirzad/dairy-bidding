# ADR-021: Soft deletes via deleted_at column

## Status
Accepted — Not yet implemented

## Context
Hard `DELETE` is irreversible, conflicts with audit requirements, and causes GDPR compliance problems (you must prove data was deleted at a specific time, not just that it is absent). For bids, deletion may be legally prohibited in some jurisdictions.

## Decision
All critical tables include a `deleted_at TIMESTAMPTZ NULL` column. Deletion sets this to the current timestamp. Queries filter `WHERE deleted_at IS NULL`. The row is never physically removed.

GDPR erasure is handled by nulling PII fields (name, email, IP) while keeping the structural record.

In EF Core, apply a global query filter:
```csharp
modelBuilder.Entity<Bid>()
    .HasQueryFilter(b => b.DeletedAt == null);
```

**Rejected**: Hard `DELETE` (irreversible, GDPR compliance issues).

## Consequences
- (+) Full audit trail of when records were "deleted" and by whom
- (+) GDPR erasure compliant: PII wiped, structural record retained for legal/financial audit
- (+) Accidental deletion is recoverable
- (-) All queries must include `WHERE deleted_at IS NULL` — enforce via EF Core global query filters to prevent accidental omission
- (-) Table size grows unboundedly; requires a periodic hard-delete job for truly safe-to-purge data
