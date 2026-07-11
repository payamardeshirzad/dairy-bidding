# ADR-030: PostgreSQL INET type for IP addresses (fraud detection)

## Status
Accepted — Not yet implemented

## Context
Shill bidding rings operate from the same network. Storing IP addresses as `VARCHAR` allows equality checks but not subnet-level queries. Detecting "multiple bidders from the same /24 subnet" requires parsing strings in application code.

## Decision
`bids.ip_address` (and equivalent columns) are stored as PostgreSQL's native `INET` type:

```sql
ip_address INET NOT NULL
```

This enables subnet-level queries directly in SQL:
```sql
-- Find all bids from the same /24 subnet as a suspicious bid
SELECT * FROM bids WHERE ip_address << '192.168.1.0/24';
```

**Rejected**: `VARCHAR` (no subnet operations, no automatic validation).

## Consequences
- (+) Subnet-level fraud pattern queries possible in SQL without application-layer parsing
- (+) Automatic validation — invalid IP strings are rejected at insert
- (+) Supports both IPv4 and IPv6 transparently
- (-) Not portable to databases that lack a native `INET` type
- (-) Npgsql maps `INET` to `IPAddress` in .NET — requires `using System.Net;` in entity classes
