# ADR-013: Frontend not containerised; always run via Vite dev server

## Status
Accepted — Implemented

## Context
Running the React frontend in a Docker/Podman container during development eliminates hot module replacement (HMR), making the inner loop (edit → see change) significantly slower. The nginx container build is needed only for production deployments.

## Decision
`dairy-bidding-web` is **removed from `compose.yml`**. The frontend is always started locally with:

```
cd src/dairy-bidding-web
npm run dev
```

The Vite dev server proxies `/api` and `/connect` requests to the API Gateway (`http://localhost:5000`) to avoid CORS issues during development.

The nginx multi-stage `Dockerfile` in `src/dairy-bidding-web/` is retained for production/staging builds.

**Rejected**: nginx container in compose for frontend during local dev.

## Consequences
- (+) HMR: browser updates in <100ms on save during development
- (+) No Docker build required for frontend changes
- (-) Frontend developers must have Node.js installed locally
- (-) The compose stack and the frontend are started separately — documented in `docs/local-setup.md`
