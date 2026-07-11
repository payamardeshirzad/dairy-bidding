# ADR-014: Observability — OpenTelemetry + Prometheus + Jaeger + Grafana

## Status
Accepted — Implemented

## Context
Microservices make it difficult to trace a single request across service boundaries and to detect degradation before users report it. A vendor-neutral, unified observability stack avoids cloud lock-in and keeps all signals (traces, metrics, logs) in one place.

## Decision
All services instrument via **OpenTelemetry**:

- **Traces**: OTLP gRPC to Jaeger (`localhost:4317`). `AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`, `AddEntityFrameworkCoreInstrumentation()`.
- **Metrics**: `GET /metrics` via `OpenTelemetry.Exporter.Prometheus.AspNetCore` (beta). `AddAspNetCoreInstrumentation()`, `AddRuntimeInstrumentation()`.
- **Prometheus** scrapes all services every 15 s (config: `infra/prometheus/prometheus.yml`).
- **Grafana** dashboards provisioned from `infra/grafana/dashboards/`. Prometheus datasource auto-provisioned from `infra/grafana/datasources/prometheus.yml`.

Prometheus `host.docker.internal` target with `extra_hosts: host-gateway` handles Podman rootless networking for scraping host-side services.

**Rejected**: Application Insights only (Azure lock-in); Serilog-only logging (no metrics/traces).

## Consequences
- (+) Vendor-neutral — works without any cloud dependency
- (+) Full traces + metrics + dashboards from day one
- (+) `traceparent` propagated across all service boundaries automatically
- (-) Jaeger all-in-one uses in-memory storage by default — traces lost on container restart
- (-) `/metrics` endpoint must be network-restricted in production (not exposed publicly)
