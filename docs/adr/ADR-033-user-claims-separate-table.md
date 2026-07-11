# ADR-033: User claims in a separate table, not columns on users

## Status
Accepted — Not yet implemented

## Context
IdentityService needs to issue tokens with user-specific claims (roles, permissions, verified-seller status, preferred locale). Adding a column for each claim type to the `users` table requires schema migrations for every new claim. Claims also have a natural cardinality: a user can have multiple roles.

## Decision
`user_claims` is a separate key-value table:

```sql
CREATE TABLE user_claims (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    claim_type VARCHAR(128) NOT NULL,
    claim_value VARCHAR(512) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_user_claims_user_id ON user_claims(user_id);
```

IdentityService reads all claims at token issuance:
```sql
SELECT claim_type, claim_value FROM user_claims WHERE user_id = @userId;
```

New claim types require no schema change — only new rows.

**Rejected**: Per-column claims on `users` table (schema migration per new claim type); single JSONB `claims` column on `users` (no fine-grained indexing, harder to audit individual claims).

## Consequences
- (+) New claim types require no schema migration
- (+) Multiple values per claim type supported naturally (multi-valued roles)
- (+) Claims are individually auditable rows
- (-) Extra JOIN at token issuance (negligible — well-indexed on `user_id`)
