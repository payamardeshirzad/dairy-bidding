# ADR-035: Polymorphic reference pattern in the notifications table

## Status
Accepted — Not yet implemented

## Context
NotificationService sends notifications triggered by different domain events: auction status changes, bid outbid events, payment confirmations. Creating separate tables (`auction_notifications`, `bid_notifications`, `payment_notifications`) leads to join complexity and repeated schema patterns. An overly wide notifications table with nullable foreign keys per entity type is fragile.

## Decision
`notifications` uses a **polymorphic reference** pattern:

```sql
CREATE TYPE reference_type_enum AS ENUM ('auction', 'bid', 'payment');

CREATE TABLE notifications (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    reference_id UUID NOT NULL,
    reference_type reference_type_enum NOT NULL,
    notification_type VARCHAR(64) NOT NULL,
    message TEXT NOT NULL,
    is_read BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_notifications_user ON notifications(user_id) WHERE is_read = FALSE;
```

Application code resolves `reference_id` to the correct table based on `reference_type`.

**Rejected**: Separate table per notification type (schema proliferation); nullable FK columns per entity type (fragile — allows invalid state where `reference_type = 'bid'` but `bid_id IS NULL`).

## Consequences
- (+) Single table handles all notification types
- (+) New `reference_type` values require only a migration to the enum — no new tables
- (-) No DB-level FK enforcement on `reference_id` — must be validated at application layer
- (-) `reference_type` enum migrations require downtime or careful rolling migration strategy
