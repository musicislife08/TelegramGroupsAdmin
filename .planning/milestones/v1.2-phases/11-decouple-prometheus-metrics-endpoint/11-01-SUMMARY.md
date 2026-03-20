---
phase: 11-decouple-prometheus-metrics-endpoint
plan: 01
subsystem: infra
tags: [opentelemetry, prometheus, metrics, observability, env-var]

requires: []
provides:
  - Decoupled Prometheus /metrics endpoint with dual env-var OR gate (ENABLE_METRICS or OTEL_EXPORTER_OTLP_ENDPOINT)
  - Independent OTEL metrics pipeline vs tracing pipeline
  - Updated CLAUDE.md documentation for metrics-only deployment mode
affects: []

tech-stack:
  added: []
  patterns:
    - "Split OTEL pipelines: metrics and tracing registered as separate AddOpenTelemetry() calls on the same IServiceCollection (safe in OTEL .NET Extensions.Hosting)"
    - "metricsEnabled OR gate: single bool shared between DI registration and endpoint mapping blocks"

key-files:
  created: []
  modified:
    - TelegramGroupsAdmin/Program.cs
    - CLAUDE.md

key-decisions:
  - "ENABLE_METRICS activates /metrics and OTEL meters without requiring OTEL_EXPORTER_OTLP_ENDPOINT — hosting providers can enable metrics scraping without deploying Seq/OTLP"
  - "AddOtlpExporter() on metrics pipeline is conditional on otlpEndpoint (not always registered when metricsEnabled)"
  - "ConfigureResource() called in both blocks with identical values — safe harmless duplication, guarantees resource attributes regardless of env var combination"
  - "No auth on /metrics endpoint — infrastructure (firewall/proxy) controls access per STAT-05"
  - "Startup log includes activation source ('via ENABLE_METRICS' or 'via OTEL_EXPORTER_OTLP_ENDPOINT') for operational clarity"

patterns-established:
  - "Dual env-var OR gate: metricsEnabled bool shared between DI and middleware blocks, evaluated once at startup"

requirements-completed: [STAT-01, STAT-02, STAT-03, STAT-04, STAT-05]

duration: 10min
completed: 2026-03-19
---

# Phase 11 Plan 01: Decouple Prometheus Metrics Endpoint Summary

**Decoupled Prometheus /metrics endpoint via new ENABLE_METRICS env var, splitting the single OTEL block into independent metrics and tracing pipelines so hosting providers can scrape metrics without deploying Seq/OTLP infrastructure**

## Performance

- **Duration:** 10 min
- **Started:** 2026-03-19T23:19:00Z
- **Completed:** 2026-03-19T23:29:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Split monolithic OTEL configuration into separate metrics pipeline (ENABLE_METRICS or OTEL_EXPORTER_OTLP_ENDPOINT) and tracing pipeline (OTEL_EXPORTER_OTLP_ENDPOINT only)
- ENABLE_METRICS env var enables /metrics and all OTEL meters without requiring Seq/OTLP infrastructure
- OTEL_EXPORTER_OTLP_ENDPOINT still implicitly enables /metrics (no breaking change for existing deployments)
- Startup log identifies which env var activated the metrics endpoint
- Updated CLAUDE.md with new env var, metrics-only mode documentation, and corrected /metrics 404 troubleshooting

## Task Commits

Each task was committed atomically:

1. **Task 1: Split OTEL registration into independent metrics and tracing pipelines** - `a28b119b` (feat)
2. **Task 2: Update CLAUDE.md documentation for ENABLE_METRICS env var** - `08db9856` (docs)

## Files Created/Modified

- `TelegramGroupsAdmin/Program.cs` - Split OTEL block into metrics pipeline (metricsEnabled) and tracing pipeline (otlpEndpoint); added metricsEnabled OR gate; updated endpoint mapping and startup log
- `CLAUDE.md` - Added ENABLE_METRICS and OTEL_EXPORTER_OTLP_ENDPOINT to Observability env vars; documented metrics-only mode vs full OTEL mode; updated /metrics 404 troubleshooting

## Decisions Made

- **ENABLE_METRICS as independent gate**: Hosting providers need Prometheus scraping without Seq/OTLP stack. A simple env var OR gate is minimal, reversible, and doesn't require any architectural changes.
- **AddOtlpExporter() conditional on otlpEndpoint**: When ENABLE_METRICS is set alone, metrics stay local to Prometheus (no OTLP export) — matches the use case intent.
- **ConfigureResource() in both OTEL blocks**: Safe duplication (OTEL .NET Extensions.Hosting merges resource configurations from multiple AddOpenTelemetry() calls). Guarantees correct service attributes regardless of which env var combination is active.
- **No auth on /metrics**: Per STAT-05 and CONTEXT.md, access control is an infrastructure concern (reverse proxy, firewall). Adding ASP.NET auth would break the homelab/compose use case where scraping happens on the container network.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None — build passed on first attempt (0 warnings, 0 errors).

## User Setup Required

None - no external service configuration required. New `ENABLE_METRICS` env var is optional; existing deployments continue to work as before.

## Self-Check

## Self-Check: PASSED

- `TelegramGroupsAdmin/Program.cs` — FOUND
- `CLAUDE.md` — FOUND
- Commit `a28b119b` — FOUND
- Commit `08db9856` — FOUND
- `metricsEnabled` references in Program.cs: 3 (>= 3 required)
- `ENABLE_METRICS` references in Program.cs: 4 (>= 2 required)
- `ENABLE_METRICS` references in CLAUDE.md: 3 (>= 2 required)

## Next Phase Readiness

Phase 11 plan 01 complete. All 5 requirements satisfied (STAT-01 through STAT-05). No follow-on work needed from this plan.

---
*Phase: 11-decouple-prometheus-metrics-endpoint*
*Completed: 2026-03-19*
