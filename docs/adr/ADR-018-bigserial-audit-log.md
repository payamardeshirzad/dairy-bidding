# ADR-018: BIGSERIAL primary key for append-only audit log

## Status
Accepted — Not yet implemented

## Context
The `payment_audit_log` is tamper-evident by design. An audit table with UUID PKs cannot prove rows were not deleted — gaps in a random UUID sequence are invisible and undetectable.

## Decision
`payment_audit_log.id` is `BIGSERIAL`. A gap in the sequential integer sequence is detectable evidence that a row was deleted, violating the audit contract.

```sql
CREATE TABLE payment_audit_log (
    id BIGSERIAL PRIMARY KEY,
    ...
);
```

**Rejected**: UUID PK on audit tables (gaps undetectable — no tamper evidence).

## Consequences
- (+) Tamper evidence built into the data structure — a missing sequence number proves deletion
- (+) Sequential scan of audit history is natural insertion order
- (-) Leaks row count of audit events (acceptable: this table is internal, never exposed via API)
- **Pending**: PaymentService not yet implemented (see ADR-038)
