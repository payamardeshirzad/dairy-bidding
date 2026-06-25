# Local Environment Setup (Podman)

This document describes how to run the full local infrastructure for the Dairy Bidding platform using **Podman** (no Docker required).

---

## 1) Prerequisites

- **Podman** installed and working
- **Podman Compose** available (`podman compose ...`)
- Windows Terminal / PowerShell (or equivalent shell)
- Ports available on localhost:
  - `5432` (PostgreSQL)
  - `6379` (Redis)
  - `5672`, `15672` (RabbitMQ + UI)
  - `9000`, `9001` (MinIO API + Console)
  - `8025`, `1025` (Mailpit UI + SMTP)
  - `9090` (Prometheus)
  - `3000` (Grafana)
  - `16686`, `4317`, `4318` (Jaeger UI + OTLP)
  - `9200`, `9300` (Elasticsearch)

### Quick validation commands

```powershell
podman --version
podman compose version
podman ps
```

---

## 2) Start local infrastructure

From project root (where `compose.yml` exists):

```powershell
podman compose up -d
```

Check running containers:

```powershell
podman ps
```

Check logs:

```powershell
podman compose logs -f
```

---

## 3) Stop / reset commands

### Stop all services (keep data volumes)

```powershell
podman compose down
```

### Stop all services and delete volumes (full reset)

```powershell
podman compose down -v
```

### Restart all services

```powershell
podman compose down
podman compose up -d
```

---

## 4) Local service URLs and credentials

## Core services

- **RabbitMQ Management UI**: http://localhost:15672  
  - Username: `dairy`
  - Password: `dairy_local_pass`
- **RabbitMQ AMQP**: `localhost:5672`

- **PostgreSQL**: `localhost:5432`  
  - Username: `dairy_admin`
  - Password: `dairy_local_pass`
  - Default DB: `postgres`
  - Service DBs created:
    - `bidding_db`
    - `auction_db`
    - `catalog_db`
    - `identity_db`
    - `payment_db`
    - `notification_db`

- **Redis**: `localhost:6379`

## Supporting services

- **MinIO API**: http://localhost:9000
- **MinIO Console**: http://localhost:9001  
  - Username: `dairy_minio`
  - Password: `dairy_minio_pass`

- **Mailpit UI**: http://localhost:8025
- **Mailpit SMTP**: `localhost:1025`

## Observability

- **Prometheus**: http://localhost:9090
- **Grafana**: http://localhost:3000  
  - Username: `admin`
  - Password: `admin`
- **Jaeger UI**: http://localhost:16686
- **OTLP endpoints**:
  - gRPC: `localhost:4317`
  - HTTP: `localhost:4318`

## Search

- **Elasticsearch**: http://localhost:9200

---

## 5) Healthcheck command

Run the healthcheck script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\healthcheck.ps1
```

Recommended after every fresh startup or environment change.

---

## 6) Verification commands (quick smoke tests)

### PostgreSQL

```powershell
podman exec -it dairy-postgres psql -U dairy_admin -d postgres -c "\l"
```

### Redis

```powershell
podman exec -it dairy-redis redis-cli ping
```

Expected: `PONG`

### RabbitMQ API

```powershell
curl -u dairy:dairy_local_pass http://localhost:15672/api/overview
```

### Elasticsearch health

```powershell
curl http://localhost:9200/_cluster/health
```

Expected status: `green` or `yellow`.

---

## 7) Troubleshooting notes

These were the key issues encountered/considered during setup:

1. **Podman was trying to use Docker Toolbox `docker-compose.exe`**
   - Symptom: TLS-related compose error from Docker Toolbox path.
   - Fix: Ensure Podman uses `podman-compose`/`podman compose`, and remove old Docker Toolbox path from environment if needed.

2. **`podman-compose` not found in PATH**
   - Symptom: `exec: "podman-compose": executable file not found in %PATH%`
   - Fix: Install via pip and ensure Python Scripts directory is in PATH.

3. **Editor warning on Grafana datasource (`apiVersion: 1`)**
   - Symptom: “Property apiVersion is not allowed”.
   - Cause: YAML schema mismatch in editor (not Grafana runtime issue).
   - Fix: Keep file as Grafana provisioning format; warning can be ignored if runtime works.

4. **Elasticsearch can be memory-sensitive**
   - Current stable config uses reduced heap:
     - `ES_JAVA_OPTS: "-Xms512m -Xmx512m"`

---

## 8) Current status

✅ All infrastructure services started successfully with Podman  
✅ Commands and service verification checks passed  
✅ Environment is ready for implementation planning
