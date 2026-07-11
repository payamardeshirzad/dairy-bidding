# ADR-015: Podman (rootless) over Docker for local infrastructure

## Status
Accepted — Implemented

## Context
The project requires local instances of PostgreSQL, RabbitMQ, Prometheus, Grafana, Jaeger, and pgAdmin. Docker Desktop requires a commercial licence for teams above a certain size. Docker Engine (Linux daemon) runs as root, which is a security concern.

## Decision
Use **Podman** (rootless, daemonless) via `podman compose` / `python -m podman_compose` to manage local infrastructure from `compose.yml`.

Key configuration details:
- All images prefixed with `docker.io/` to work without Docker Hub as the implicit registry
- `:Z` SELinux volume label on bind mounts (required on RHEL/Fedora-based systems)
- `extra_hosts: host-gateway` on the Prometheus container so it can scrape host-side services (Podman rootless network)
- `compose.yml` is compatible with both `podman-compose` and `docker compose`

**Rejected**: Docker Desktop (commercial licence), Docker Engine daemon (runs as root).

## Consequences
- (+) No daemon running as root — reduced attack surface
- (+) No licensing restrictions
- (+) Same `compose.yml` works with `docker compose` for teams that prefer Docker
- (-) `podman-compose` does not support all Compose v3 features
- (-) `:Z` volume label required on SELinux-enforcing systems — can cause confusion on Windows/macOS
