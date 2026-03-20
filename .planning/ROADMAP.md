# Roadmap: TelegramGroupsAdmin

## Milestones

- ✅ **v1.0 Dead Code Removal** — Phases 1-5 (shipped 2026-03-17)
- ✅ **v1.1 Bug Fix Sweep** — Phases 6-8.1 (shipped 2026-03-18)
- 🚧 **v1.2 SaaS Hosting Readiness** — Phases 9-12 (in progress)

## Phases

<details>
<summary>✅ v1.0 Dead Code Removal (Phases 1-5) — SHIPPED 2026-03-17</summary>

- [x] Phase 1: Core and Configuration (1/1 plans) — completed 2026-03-16
- [x] Phase 2: Data and Mapping Models (1/1 plans) — completed 2026-03-16
- [x] Phase 3: Telegram Project (2/2 plans) — completed 2026-03-16
- [x] Phase 4: Main Application (2/2 plans) — completed 2026-03-16
- [x] Phase 5: ContentDetection Project (2/2 plans) — completed 2026-03-17

</details>

<details>
<summary>✅ v1.1 Bug Fix Sweep (Phases 6-8.1) — SHIPPED 2026-03-18</summary>

- [x] Phase 6: Data Layer Fixes (2/2 plans) — completed 2026-03-17
- [x] Phase 7: Backend Service Fixes (2/2 plans) — completed 2026-03-17
- [x] Phase 8: Frontend Fixes (4/4 plans) — completed 2026-03-17
- [x] Phase 8.1: Fix review-all findings (1/1 plans) — completed 2026-03-18

</details>

### 🚧 v1.2 SaaS Hosting Readiness (In Progress)

**Milestone Goal:** Make TGA deployable by an external hosting orchestrator without adding SaaS-specific code to the open-source codebase. Three independent capabilities: infrastructure env var override for ClamAV, headless owner account bootstrapping, and decoupled Prometheus metrics endpoint.

- [x] **Phase 9: ClamAV Environment Variable Override** - Shared ClamAV daemon support via CLAMAV_HOST/CLAMAV_PORT env vars (completed 2026-03-18)
- [x] **Phase 10: Bootstrap Owner CLI Flag** - Headless Owner account creation for Kubernetes init container pattern (completed 2026-03-19)
- [x] **Phase 11: Decouple Prometheus Metrics Endpoint** - ENABLE_METRICS env var decouples /metrics from OTEL_EXPORTER_OTLP_ENDPOINT for hosting provider monitoring (completed 2026-03-20)
- [ ] **Phase 12: ClamAV IOptions Refactor + Doc Fixes** - Refactor ClamAV env var override to use IOptions<ClamAVConfig> pattern; fix stale docs

## Phase Details

### Phase 9: ClamAV Environment Variable Override
**Goal**: Operators can point all TGA instances at a shared ClamAV daemon without pre-seeding the database on each instance
**Depends on**: Nothing (independent feature, first phase of v1.2)
**Requirements**: CLAM-01, CLAM-02, CLAM-03, CLAM-04
**Success Criteria** (what must be TRUE):
  1. When CLAMAV_HOST and CLAMAV_PORT are both set, every file scan uses those values instead of what is stored in the database
  2. When only one of CLAMAV_HOST or CLAMAV_PORT is set, the DB-stored values are used unchanged (no partial override)
  3. The first scan that uses the env var override writes one INFO log line showing the effective host:port
  4. The ClamAV health check (`GetHealthAsync`) connects to the same host:port that `ScanFileAsync` would use
**Plans:** 1/1 plans complete
Plans:
- [ ] 09-01-PLAN.md — TDD: ClamAV env var override (CLAM-01 through CLAM-04)

### Phase 10: Bootstrap Owner CLI Flag
**Goal**: A hosting orchestrator can create a fully login-ready Owner account before the instance is internet-facing, with safe retry semantics for Kubernetes init containers
**Depends on**: Nothing (independent feature)
**Requirements**: BOOT-01, BOOT-02, BOOT-03, BOOT-04, BOOT-05, BOOT-06, BOOT-07
**Success Criteria** (what must be TRUE):
  1. Running `--bootstrap /path/to/bootstrap.json` (JSON with email + password) creates an Owner account that can log in via browser on first run
  2. Running the same command a second time exits 0 with an "already bootstrapped" log message — DB-first check, no file read needed on subsequent runs
  3. Running `--bootstrap` with a missing, empty, or invalid JSON file exits 1 with a clear error message
  4. The bootstrapped account has EmailVerified=true and TOTP setup forced on first browser login (TotpEnabled=true, TotpSecret=null)
  5. An audit log entry is written recording the Owner account creation (non-fatal — exit 0 if user created even if audit fails)
**Plans:** 1/1 plans complete
Plans:
- [ ] 10-01-PLAN.md — TDD: Bootstrap Owner CLI flag (BOOT-01 through BOOT-07)

### Phase 11: Decouple Prometheus Metrics Endpoint
**Goal**: A hosting provider can enable the Prometheus `/metrics` endpoint via `ENABLE_METRICS` env var without requiring the full OTEL observability stack (`OTEL_EXPORTER_OTLP_ENDPOINT`)
**Depends on**: Nothing (independent — decouples existing functionality)
**Requirements**: STAT-01, STAT-02, STAT-03, STAT-04, STAT-05
**Success Criteria** (what must be TRUE):
  1. Setting `ENABLE_METRICS` env var maps the `/metrics` Prometheus endpoint and registers OTEL meters, even without `OTEL_EXPORTER_OTLP_ENDPOINT`
  2. Setting `OTEL_EXPORTER_OTLP_ENDPOINT` still implicitly enables `/metrics` (no breaking change for existing deployments)
  3. When only `ENABLE_METRICS` is set (no `OTEL_EXPORTER_OTLP_ENDPOINT`), logging/tracing to Seq are not configured — only meters and Prometheus exporter
  4. Startup INFO log indicates which env var activated the metrics endpoint
**Plans:** 1/1 plans complete
Plans:
- [ ] 11-01-PLAN.md — Decouple metrics pipeline from OTEL tracing (STAT-01 through STAT-05)

### Phase 12: ClamAV IOptions Refactor + Doc Fixes
**Goal**: Refactor ClamAV env var override from raw `Environment.GetEnvironmentVariable` to `IOptions<ClamAVConfig>` pattern, aligning with ASP.NET Core conventions and making compose `CLAMAV__HOST`/`CLAMAV__PORT` the correct mechanism. Fix stale documentation.
**Depends on**: Phase 9 (refactors its implementation)
**Requirements**: CLAM-01 (updated), CLAM-02, CLAM-03, CLAM-04
**Gap Closure:** Closes CLAM-COMPOSE-ENV integration gap + doc fixes from v1.2 audit
**Success Criteria** (what must be TRUE):
  1. `ClamAVScannerService` reads ClamAV override from `IOptions<ClamAVConfig>` (bound via `IConfiguration`) instead of raw `Environment.GetEnvironmentVariable`
  2. When `CLAMAV__HOST` and `CLAMAV__PORT` env vars are set, the override activates (both required, no partial override preserved)
  3. Existing compose files' `CLAMAV__HOST`/`CLAMAV__PORT` entries work correctly without modification
  4. Unit tests updated to configure via IOptions instead of mocking env vars
  5. REQUIREMENTS.md CLAM-01 updated to remove "per-scan, not cached at startup" clause
  6. REQUIREMENTS.md STAT-01/02/03 fixed to reference `OTEL_EXPORTER_OTLP_ENDPOINT` instead of stale `SEQ_URL`
  7. Production compose template includes `ENABLE_METRICS` env var example
Plans:
- [ ] 12-01-PLAN.md — ClamAV IOptions refactor + doc fixes

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Core and Configuration | v1.0 | 1/1 | Complete | 2026-03-16 |
| 2. Data and Mapping Models | v1.0 | 1/1 | Complete | 2026-03-16 |
| 3. Telegram Project | v1.0 | 2/2 | Complete | 2026-03-16 |
| 4. Main Application | v1.0 | 2/2 | Complete | 2026-03-16 |
| 5. ContentDetection Project | v1.0 | 2/2 | Complete | 2026-03-17 |
| 6. Data Layer Fixes | v1.1 | 2/2 | Complete | 2026-03-17 |
| 7. Backend Service Fixes | v1.1 | 2/2 | Complete | 2026-03-17 |
| 8. Frontend Fixes | v1.1 | 4/4 | Complete | 2026-03-17 |
| 8.1. Fix review-all findings | v1.1 | 1/1 | Complete | 2026-03-18 |
| 9. ClamAV Environment Variable Override | v1.2 | 1/1 | Complete | 2026-03-18 |
| 10. Bootstrap Owner CLI Flag | v1.2 | 1/1 | Complete | 2026-03-19 |
| 11. Decouple Prometheus Metrics Endpoint | v1.2 | 1/1 | Complete | 2026-03-20 |
| 12. ClamAV IOptions Refactor + Doc Fixes | v1.2 | 0/1 | Planned | - |
