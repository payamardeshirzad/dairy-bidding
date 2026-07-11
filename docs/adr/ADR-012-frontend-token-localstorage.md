# ADR-012: Frontend token storage in localStorage with expiration check

## Status
Accepted — Implemented (XSS risk formally closed by ADR-036 BFF pattern)

## Context
The React SPA needs to persist the JWT between page navigations. Options:

| Storage | XSS risk | Survives refresh | CSRF risk |
|---|---|---|---|
| `localStorage` | Yes (any injected script reads it) | Yes | No |
| `sessionStorage` | Yes | No (lost on tab close) | No |
| `httpOnly` cookie | No | Yes | Yes (mitigable with SameSite) |
| In-memory (React state) | No | No (lost on reload) | No |

## Decision
JWT stored in `localStorage` with an `expiresAtUtc` field. On every read, expiry is checked and an expired token is treated as absent (user must re-authenticate).

```ts
localStorage.setItem('token', JSON.stringify({ value: jwt, expiresAtUtc: exp }));
```

**Rejected**: `sessionStorage` (lost on tab close — poor UX for an auction platform where users monitor lots); `httpOnly` cookies (requires CORS cookie config, same-site policy, and a BFF — deferred to ADR-036).

## Consequences
- (+) Simplest implementation; survives page refresh without a re-auth round-trip
- (-) XSS vulnerability: any injected script can steal the token from `localStorage`
- (-) This risk is **accepted** as tolerable for an internal platform at this stage
- **The risk is formally closed by ADR-036**: once the BFF pattern is adopted, tokens move to `httpOnly` session cookies and are never exposed to JavaScript
