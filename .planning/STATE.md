---
gsd_state_version: 1.0
milestone: v1.2
milestone_name: SaaS Hosting Readiness
status: planning
stopped_at: Completed 12-clamav-ioptions-refactor-doc-fixes/12-01-PLAN.md
last_updated: "2026-03-20T04:32:16.431Z"
last_activity: 2026-03-18 — Roadmap created, 16/16 v1.2 requirements mapped across 3 phases
progress:
  total_phases: 4
  completed_phases: 4
  total_plans: 4
  completed_plans: 4
  percent: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-18)

**Core value:** Reliable, automated Telegram group moderation with a responsive web UI — correctness and operational simplicity above all
**Current focus:** v1.2 SaaS Hosting Readiness — Phase 9 ready to plan

## Current Position

Phase: 9 of 11 (ClamAV Environment Variable Override)
Plan: — (not yet planned)
Status: Ready to plan
Last activity: 2026-03-18 — Roadmap created, 16/16 v1.2 requirements mapped across 3 phases

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**
- Total plans completed: 0 (this milestone)
- Average duration: —
- Total execution time: —

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

*Updated after each plan completion*
| Phase 09-clamav-environment-variable-override P01 | 15min | 1 tasks | 2 files |
| Phase 10-bootstrap-owner-cli-flag P01 | 10min | 2 tasks | 8 files |
| Phase 11-decouple-prometheus-metrics-endpoint P01 | 10min | 2 tasks | 2 files |
| Phase 12-clamav-ioptions-refactor-doc-fixes P01 | 2min | 2 tasks | 3 files |

## Accumulated Context

### Decisions

- [v1.2]: CLI bootstrap over API bootstrap — runs before instance is internet-facing, no race condition, fits existing --migrate-only pattern
- [v1.2]: Env var override for ClamAV only — infrastructure concern; other settings are app config managed via UI
- [v1.2]: Status endpoint with STATUS_API_KEY gate — simple JSON polling for hosting providers, not Prometheus firehose
- [v1.2]: No SaaS-specific code in OSS repo — extensibility via CLI flags, env vars, HTTP endpoints
- [Phase 09]: Static volatile bool _hasLoggedOverride for one-time override log: Scoped service requires static field for per-process semantic
- [Phase 09]: GetEffectiveEndpointAsync centralizes env var check — both non-whitespace + valid int required for override to apply
- [Phase 09]: localhost:1 in tests for fast TCP failure (connection refused); sentinel-throw for CLAM-02 DB-consulted verification without TCP
- [Phase 10]: AnyUsersExistAsync uses AnyAsync (stops at first row) for efficient bootstrap idempotency check
- [Phase 10]: BootstrapOwnerService is a static class (not injectable) — Program.cs passes resolved services directly, keeps service layer clean
- [Phase 10]: EmailVerified=true unconditionally on bootstrap (no featureAvailability check) — K8s init container must produce login-ready account
- [Phase Phase 11]: ENABLE_METRICS activates /metrics independently of OTEL_EXPORTER_OTLP_ENDPOINT — hosting providers can enable Prometheus scraping without Seq/OTLP infrastructure
- [Phase Phase 11]: AddOtlpExporter() on metrics pipeline conditional on otlpEndpoint — ENABLE_METRICS alone produces local-only Prometheus metrics with no OTLP export
- [Phase 12]: Compose env var fix is docs-only — ClamAVScannerService already reads correct single-underscore names; compose files were wrong, not code
- [Phase 12]: ENABLE_METRICS documented in both compose files as commented examples with appropriate context for each deployment scenario

### Blockers/Concerns

- [Phase 11]: Phase pivoted from custom JSON /healthz/status endpoint to decoupling /metrics from SEQ_URL via ENABLE_METRICS env var. No app-level auth, no bot state, no custom JSON schema.

## Session Continuity

Last session: 2026-03-20T04:19:15.923Z
Stopped at: Completed 12-clamav-ioptions-refactor-doc-fixes/12-01-PLAN.md
Resume file: None
