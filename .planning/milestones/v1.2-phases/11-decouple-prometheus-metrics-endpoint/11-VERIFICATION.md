---
phase: 11-decouple-prometheus-metrics-endpoint
verified: 2026-03-19T23:45:00Z
status: passed
score: 5/5 must-haves verified
re_verification: false
human_verification:
  - test: "ENABLE_METRICS alone activates /metrics endpoint"
    expected: "curl http://localhost:<PORT>/metrics returns 200 with Prometheus text format output"
    why_human: "Requires running app with ENABLE_METRICS=1 (and no OTEL_EXPORTER_OTLP_ENDPOINT) — CRITICAL RULES prohibit running the app in normal mode"
  - test: "OTEL_EXPORTER_OTLP_ENDPOINT alone still activates /metrics (no breaking change)"
    expected: "curl http://localhost:<PORT>/metrics returns 200 with Prometheus output when only OTEL_EXPORTER_OTLP_ENDPOINT is set"
    why_human: "Runtime behavior verification — requires live app instance"
  - test: "Metrics-only mode does not attempt OTLP export"
    expected: "No connection errors or warnings about unreachable OTLP endpoint in startup logs when only ENABLE_METRICS is set"
    why_human: "Requires observing live startup log output with ENABLE_METRICS set and no OTLP endpoint configured"
  - test: "Startup log shows activation source"
    expected: "Log line contains 'Prometheus metrics endpoint mapped to /metrics (via ENABLE_METRICS)' or 'via OTEL_EXPORTER_OTLP_ENDPOINT'"
    why_human: "Log output is only observable at runtime"
---

# Phase 11: Decouple Prometheus Metrics Endpoint Verification Report

**Phase Goal:** A hosting provider can enable the Prometheus `/metrics` endpoint via `ENABLE_METRICS` env var without requiring the full OTEL observability stack (`OTEL_EXPORTER_OTLP_ENDPOINT`). No breaking changes for existing deployments.
**Verified:** 2026-03-19T23:45:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Setting `ENABLE_METRICS` env var maps `/metrics` and registers OTEL meters without requiring `OTEL_EXPORTER_OTLP_ENDPOINT` | VERIFIED | `metricsEnabled` OR gate at Program.cs:149-150; metrics block at line 155; endpoint mapping at line 361 |
| 2 | Setting `OTEL_EXPORTER_OTLP_ENDPOINT` still implicitly enables `/metrics` (no breaking change) | VERIFIED | `metricsEnabled = !string.IsNullOrEmpty(otlpEndpoint) \|\| ...` at Program.cs:149 preserves prior gate |
| 3 | When only `ENABLE_METRICS` is set, Serilog-to-Seq and OTEL tracing are NOT configured | VERIFIED | Serilog Seq sink gated on `seqUrl` (line 98); tracing block gated on `otlpEndpoint` (line 179); both independent of `metricsEnabled` |
| 4 | Startup INFO log indicates which env var activated the metrics endpoint | VERIFIED | Program.cs:364-368 — `activatedBy` variable set to `"OTEL_EXPORTER_OTLP_ENDPOINT"` or `"ENABLE_METRICS"`; passed to `LogInformation` |
| 5 | No auth middleware is added to the `/metrics` endpoint | VERIFIED | Program.cs:361-369 — `MapPrometheusScrapingEndpoint()` has no chained `.RequireAuthorization()` or `.RequireAuthentication()`; grep confirms absence |

**Score:** 5/5 truths verified (code level)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `TelegramGroupsAdmin/Program.cs` | Decoupled metrics pipeline with dual env-var OR gate | VERIFIED | Contains `ENABLE_METRICS` (4 occurrences), `metricsEnabled` (3 occurrences); two independent OTEL blocks; build passes with 0 warnings |
| `CLAUDE.md` | Updated documentation for metrics env var and troubleshooting | VERIFIED | Contains `ENABLE_METRICS` (3 occurrences); documents metrics-only mode; `/metrics 404` troubleshooting updated to reference both env vars |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `Program.cs` service registration (metrics block) | `Program.cs` endpoint mapping | `metricsEnabled` bool shared between DI and middleware blocks | VERIFIED | `metricsEnabled` declared at line 149, consumed in `if (metricsEnabled)` block at line 155 (DI) and again at line 361 (endpoint mapping) — single declaration, two uses |
| Metrics pipeline (`AddPrometheusExporter`) | OTLP export conditional | `if (!string.IsNullOrEmpty(otlpEndpoint))` inside metrics block | VERIFIED | Program.cs:173-174 — `AddOtlpExporter()` only called when `otlpEndpoint` is non-null, even when `metricsEnabled` is true |
| Tracing pipeline | `OTEL_EXPORTER_OTLP_ENDPOINT` only | Independent `if (!string.IsNullOrEmpty(otlpEndpoint))` block | VERIFIED | Program.cs:179-194 — separate `AddOpenTelemetry().WithTracing()` block gated solely on `otlpEndpoint` |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| STAT-01 | 11-01 | `ENABLE_METRICS` gates `/metrics` independently of `OTEL_EXPORTER_OTLP_ENDPOINT` | VERIFIED | Program.cs:149-150 OR gate; endpoint mapping at 361; verified by code inspection |
| STAT-02 | 11-01 | `ENABLE_METRICS` alone: OTEL meters + Prometheus exporter registered; Seq/tracing NOT configured | VERIFIED | Metrics block (155-176) activates on `metricsEnabled`; Serilog Seq sink (98-101) on `seqUrl`; tracing (179-194) on `otlpEndpoint` — fully independent |
| STAT-03 | 11-01 | `OTEL_EXPORTER_OTLP_ENDPOINT` still implicitly enables `/metrics` (no breaking change) | VERIFIED | `metricsEnabled` includes `otlpEndpoint` in OR gate; pre-phase behavior preserved — note: REQUIREMENTS.md text incorrectly names `SEQ_URL` as the prior gate; actual pre-phase gate was `OTEL_EXPORTER_OTLP_ENDPOINT` (confirmed via `git show a28b119b^`); implementation correctly preserves `OTEL_EXPORTER_OTLP_ENDPOINT` behavior |
| STAT-04 | 11-01 | Startup INFO log indicates which env var activated the endpoint | VERIFIED | Program.cs:364-368 — `activatedBy` variable, `LogInformation` with structured `{ActivatedBy}` parameter |
| STAT-05 | 11-01 | No app-level auth on `/metrics` | VERIFIED | No `.RequireAuthorization()` chained to `MapPrometheusScrapingEndpoint()` in Program.cs; grep for `RequireAuthorization.*metric` returns no results |

**Note on REQUIREMENTS.md language:** STAT-02 and STAT-03 in REQUIREMENTS.md reference `SEQ_URL` as the existing gate ("SEQ_URL still implicitly enables /metrics"). This is stale documentation — the actual pre-phase gate was `OTEL_EXPORTER_OTLP_ENDPOINT`, and `SEQ_URL` only ever controlled Serilog/Seq logging. The RESEARCH.md (Pitfall 3) correctly identified this inconsistency. The implementation and CLAUDE.md update are accurate; the REQUIREMENTS.md text naming `SEQ_URL` is the inaccuracy. The behavioral intent of the requirements (preserve existing gate, no breaking change) is fully satisfied.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None found | — | — | — | — |

No TODO/FIXME/HACK/placeholder comments found in modified files. Build: 0 warnings, 0 errors. Commits `a28b119b` and `08db9856` both present in git history.

### Human Verification Required

The automated code inspection confirms all five truths at the implementation level. The following items require runtime verification because the CRITICAL RULES prohibit running the application in normal mode, and the VALIDATION.md explicitly classifies these as manual-only.

#### 1. ENABLE_METRICS activates /metrics at runtime (STAT-01)

**Test:** Set `ENABLE_METRICS=1` (no `OTEL_EXPORTER_OTLP_ENDPOINT`), start app, curl `http://localhost:<PORT>/metrics`
**Expected:** HTTP 200 with Prometheus text format output (lines starting with `#` and metric names like `process_runtime_dotnet_gc_collections_count_total`)
**Why human:** Requires running the application

#### 2. OTEL_EXPORTER_OTLP_ENDPOINT alone still activates /metrics (STAT-03)

**Test:** Set `OTEL_EXPORTER_OTLP_ENDPOINT=http://seq:5341/ingest/otlp` (no `ENABLE_METRICS`), start app, curl `/metrics`
**Expected:** HTTP 200 with Prometheus output — existing behavior preserved
**Why human:** Regression check requires live app

#### 3. Metrics-only mode does not attempt OTLP export (STAT-02)

**Test:** Set only `ENABLE_METRICS=1`, observe startup logs
**Expected:** No OTLP connection errors or warnings about unreachable endpoint; Prometheus /metrics returns data from local pipeline only
**Why human:** OTLP absence is only observable at runtime; code path gating is verified but runtime effect requires observation

#### 4. Startup log shows activation source (STAT-04)

**Test:** Start app with `ENABLE_METRICS=1`; start app again with `OTEL_EXPORTER_OTLP_ENDPOINT=...`
**Expected:** First run logs `"via ENABLE_METRICS"`, second run logs `"via OTEL_EXPORTER_OTLP_ENDPOINT"`
**Why human:** Log output is only observable at runtime

### Gaps Summary

No code-level gaps found. All five must-have truths are satisfied at the implementation level:

- `metricsEnabled` OR gate correctly decouples `/metrics` from `OTEL_EXPORTER_OTLP_ENDPOINT`
- Serilog Seq and OTEL tracing pipelines remain independently gated on their own env vars
- `AddOtlpExporter()` on the metrics pipeline is conditional on `otlpEndpoint` (not always registered)
- `MapPrometheusScrapingEndpoint()` gated on `metricsEnabled` (not `otlpEndpoint`)
- Startup log includes structured `{ActivatedBy}` attribution
- No auth on the endpoint
- CLAUDE.md correctly documents the new env var and updates the `/metrics 404` troubleshooting

The only outstanding items are runtime smoke tests that cannot be automated per CRITICAL RULES.

---

_Verified: 2026-03-19T23:45:00Z_
_Verifier: Claude (gsd-verifier)_
