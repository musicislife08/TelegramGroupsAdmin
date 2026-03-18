# Roadmap: TelegramGroupsAdmin

## Milestones

- ✅ **v1.0 Dead Code Removal** — Phases 1-5 (shipped 2026-03-17)
- ✅ **v1.1 Bug Fix Sweep** — Phases 6-8.1 (shipped 2026-03-18)
- 🚧 **v1.2 SaaS Hosting Readiness** — Phases 1-3 (in progress)

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

**Milestone Goal:** Make TGA deployable by an external hosting orchestrator without adding SaaS-specific code to the open-source codebase. Three independent capabilities: infrastructure env var override for ClamAV, headless owner account bootstrapping, and a lightweight runtime status endpoint.

- [ ] **Phase 1: ClamAV Environment Variable Override** - Shared ClamAV daemon support via CLAMAV_HOST/CLAMAV_PORT env vars
- [ ] **Phase 2: Bootstrap Owner CLI Flag** - Headless Owner account creation for Kubernetes init container pattern
- [ ] **Phase 3: GET /healthz/status Endpoint** - API-key-gated runtime status endpoint for hosting provider monitoring

## Phase Details

### Phase 1: ClamAV Environment Variable Override
**Goal**: Operators can point all TGA instances at a shared ClamAV daemon without pre-seeding the database on each instance
**Depends on**: Nothing (independent feature, first phase of v1.2)
**Requirements**: CLAM-01, CLAM-02, CLAM-03, CLAM-04
**Success Criteria** (what must be TRUE):
  1. When CLAMAV_HOST and CLAMAV_PORT are both set, every file scan uses those values instead of what is stored in the database
  2. When only one of CLAMAV_HOST or CLAMAV_PORT is set, the DB-stored values are used unchanged (no partial override)
  3. The first scan that uses the env var override writes one INFO log line showing the effective host:port
  4. The ClamAV health check (`GetHealthAsync`) connects to the same host:port that `ScanFileAsync` would use
**Plans**: TBD

### Phase 2: Bootstrap Owner CLI Flag
**Goal**: A hosting orchestrator can create a fully login-ready Owner account before the instance is internet-facing, with safe retry semantics for Kubernetes init containers
**Depends on**: Nothing (independent feature)
**Requirements**: BOOT-01, BOOT-02, BOOT-03, BOOT-04, BOOT-05, BOOT-06, BOOT-07
**Success Criteria** (what must be TRUE):
  1. Running `--bootstrap-owner admin@example.com` with `--bootstrap-owner-password` set creates an Owner account that can log in via browser on first run
  2. Running the same command a second time exits 0 with an "already bootstrapped" log message — no duplicate account created
  3. Running `--bootstrap-owner` without `--bootstrap-owner-password` set exits 1 with a clear error message
  4. The bootstrapped account has EmailVerified=true and TOTP setup forced on first browser login (TotpEnabled=true, TotpSecret=null)
  5. An audit log entry is written recording the Owner account creation
**Plans**: TBD

### Phase 3: GET /healthz/status Endpoint
**Goal**: A hosting provider's monitoring dashboard can poll a single authenticated JSON endpoint for .NET runtime health metrics without needing access to the application internals
**Depends on**: Nothing (independent — status endpoint reports runtime metrics only, not ClamAV/DB health)
**Requirements**: STAT-01, STAT-02, STAT-03, STAT-04, STAT-05
**Success Criteria** (what must be TRUE):
  1. `GET /healthz/status` with the correct `X-Status-Api-Key` header returns a 200 JSON response containing .NET runtime metrics (memory working set, GC gen0/1/2 collections, thread pool stats, uptime)
  2. When `STATUS_API_KEY` is not configured, the `/healthz/status` route does not exist (404, not 401)
  3. A request with a wrong or missing `X-Status-Api-Key` header returns 401
  4. The API key value never appears in any log output at any log level
**Plans**: TBD

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
| 1. ClamAV Environment Variable Override | v1.2 | 0/TBD | Not started | - |
| 2. Bootstrap Owner CLI Flag | v1.2 | 0/TBD | Not started | - |
| 3. GET /healthz/status Endpoint | v1.2 | 0/TBD | Not started | - |
