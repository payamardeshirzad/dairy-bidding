# Dairy Bidding Platform — Local Dev Setup

Full guide for running the platform locally in development mode.

> Infrastructure (Postgres, RabbitMQ, Redis, etc.) runs in **Podman** containers.  
> The four .NET services run directly on the host via `dotnet run`.

---

## Architecture & ports

| Component | Technology | Dev port |
|---|---|---|
| Frontend | React 19 + Vite dev server (host) | 5173 |
| API Gateway | ASP.NET Core 9 + YARP | 5000 |
| Identity Service | ASP.NET Core 9 Minimal API | 5245 |
| Auction Service | ASP.NET Core 9 Minimal API | 5255 |
| Bidding Service | ASP.NET Core 9 Minimal API | 5170 |
| PostgreSQL | Postgres 16 (Podman) | 5432 |
| RabbitMQ | 3.13 (Podman) | 5672 / 15672 |
| Redis | 7 (Podman) | 6379 |
| pgAdmin | dpage/pgadmin4 (Podman) | 5050 |

---

## Prerequisites

| Tool | Min version | Check |
|---|---|---|
| Podman | 4+ | `podman --version` |
| Podman Compose | any | `podman compose version` |
| .NET SDK | 9.0 | `dotnet --version` |
| Node.js | 22+ | `node --version` (only needed for frontend dev mode) |
| npm | 10+ | `npm --version` (only needed for frontend dev mode) |

Ensure the following ports are free before starting:

```
App:   5000, 5170, 5245, 5255, 5173
Infra: 5432, 6379, 5672, 15672, 9000, 9001,
       8025, 1025, 9090, 3000, 16686, 4317,
       4318, 9200, 9300, 5050
```

---

## Step 1 — Start infrastructure

From the project root (where `compose.yml` lives):

```powershell
podman compose up -d
```

This starts all infrastructure services (Postgres, RabbitMQ, Redis, Prometheus, Grafana, etc.).  
Wait ~15 seconds for PostgreSQL to become healthy before starting the .NET services:

```powershell
podman ps   # all containers should show "healthy" or "running"
```

---

## Step 2 — Run the .NET services

Open four separate terminals from the project root. Start them in this order:

```powershell
# Terminal 1 — Identity Service
dotnet run --project src/DairyBidding.IdentityService

# Terminal 2 — Auction Service
dotnet run --project src/DairyBidding.AuctionService

# Terminal 3 — Bidding Service
dotnet run --project src/DairyBidding.BiddingService

# Terminal 4 — API Gateway
dotnet run --project src/DairyBidding.ApiGateway
```

Each service:
- Loads `appsettings.Development.json` automatically via its `launchSettings.json`
- Runs EF Core migrations against its own database on startup
- Logs to its terminal

---

## Step 3 — Run the frontend

```powershell
cd src/dairy-bidding-web
npm install   # first time only
npm run dev
```

The Vite dev server starts on http://localhost:5173 with hot module replacement.

---

## Step 4 — Open the app

Navigate to http://localhost:5173.

**Dev login credentials**

| Field | Value |
|---|---|
| Username | `admin` |
| Password | `admin123` |

---

## Service URLs & credentials reference

### Application

| Service | URL |
|---|---|
| Frontend | http://localhost:5173 |
| API Gateway | http://localhost:5000 |
| Identity Service | http://localhost:5245 |
| Auction Service | http://localhost:5255 |
| Bidding Service | http://localhost:5170 |

### Infrastructure

| Service | URL | Username | Password |
|---|---|---|---|
| pgAdmin | http://localhost:5050 | `admin@local.dev` | `admin123` |
| RabbitMQ Management | http://localhost:15672 | `dairy` | `dairy_local_pass` |
| MinIO Console | http://localhost:9001 | `dairy_minio` | `dairy_minio_pass` |
| Mailpit | http://localhost:8025 | — | — |
| Grafana | http://localhost:3000 | `admin` | `admin` |
| Prometheus | http://localhost:9090 | — | — |
| Jaeger | http://localhost:16686 | — | — |
| Elasticsearch | http://localhost:9200 | — | — |

### PostgreSQL

| Setting | Value |
|---|---|
| Host | `localhost` (or `dairy-postgres` from within Podman network) |
| Port | `5432` |
| Username | `dairy_admin` |
| Password | `dairy_local_pass` |

Databases: `identity_db` · `auction_db` · `bidding_db` · `catalog_db` · `payment_db` · `notification_db`

**pgAdmin server connection** — add manually after first login:  
Host: `dairy-postgres` · Port: `5432` · Username: `dairy_admin` · Password: `dairy_local_pass`

---

## Common operations

### Full reset — wipe all data volumes

```powershell
# 1. Stop all .NET processes
Get-Process dotnet | Stop-Process -Force

# 2. Tear down infra and delete volumes
podman compose down -v

# 3. Restart infra and wait for healthy
podman compose up -d
Start-Sleep 15

# 4. Restart the .NET services (Step 2 above)
```

EF Core migrations re-run automatically on the next `dotnet run`.

### Restart infra (keep data)

```powershell
podman compose down
podman compose up -d
```

### View logs

```powershell
podman compose logs -f
podman compose logs -f postgres   # single service
```

### Healthcheck

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\healthcheck.ps1
```

---

## Troubleshooting

### "Port already in use" when starting a .NET service

```powershell
Get-Process dotnet | Stop-Process -Force
```

### Services fail to connect after `podman compose down -v`

PostgreSQL takes ~15 seconds to initialise. Wait until `podman ps` shows `(healthy)` before starting the .NET services.

### pgAdmin exits immediately (exit code 1)

Check logs with `podman logs dairy-pgadmin`. Common causes:
- **Container name conflict** — `podman rm -f dairy-pgadmin`, then `podman compose up -d pgadmin`
- **Special-use email domain** (`.local`) — use a standard TLD such as `.dev` or `.com` for `PGADMIN_DEFAULT_EMAIL`

### Auction seed events not received by BiddingService

The AuctionService publishes seed events inside its `ApplicationStarted` callback. If the RabbitMQ queue was lost (e.g. after `down -v`), restart AuctionService so it re-publishes, then restart BiddingService to ensure the consumer is active.

### Podman using wrong compose binary (Docker Toolbox conflict)

Symptom: TLS-related error mentioning a Docker Toolbox path.  
Fix: Ensure Podman's compose is first in `PATH`; remove old Docker Toolbox entries.

### Elasticsearch memory pressure

Adjust `ES_JAVA_OPTS` heap values in `compose.yml` if Elasticsearch fails to start.  
Current config: `-Xms512m -Xmx512m`.

---

## Infrastructure-only reference

See [`docs/local-setup.md`](docs/local-setup.md) for full infrastructure smoke tests and per-service verification commands.