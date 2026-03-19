# Requirements: TelegramGroupsAdmin v1.2

**Defined:** 2026-03-18
**Core Value:** Reliable, automated Telegram group moderation with a responsive web UI — correctness and operational simplicity above all

## v1.2 Requirements

Requirements for SaaS hosting readiness. Each maps to roadmap phases.

### Bootstrap

- [ ] **BOOT-01**: Operator can create an Owner account headlessly via `--bootstrap <path>` CLI flag, where `<path>` points to a JSON file containing `{"email": "...", "password": "..."}`
- [ ] **BOOT-02**: Bootstrap exits 0 with INFO log when any user already exists (idempotent for orchestrator retry loops — DB check before file read, so file can be absent on subsequent runs)
- [ ] **BOOT-03**: Bootstrap exits 1 with clear error when the JSON file is missing, empty, or has invalid/incomplete content
- [ ] **BOOT-04**: Bootstrapped account has `EmailVerified=true` (no inbox for headless setup)
- [ ] **BOOT-05**: Bootstrapped account has `TotpEnabled=true`, `TotpSecret=null` (forces TOTP setup on first browser login, matching existing registration flow)
- [ ] **BOOT-06**: Bootstrap writes an audit log entry recording the owner account creation
- [ ] **BOOT-07**: Bootstrap runs after database migrations and before ML training, following existing CLI flag early-exit pattern

### ClamAV Override

- [x] **CLAM-01**: `CLAMAV_HOST` and `CLAMAV_PORT` env vars override DB-stored ClamAV host and port at scan time (per-scan, not cached at startup)
- [x] **CLAM-02**: Both env vars are required for override to activate — if either is missing, DB config is used (no partial override)
- [x] **CLAM-03**: When env var override is active, an INFO log records the effective ClamAV host:port on first use
- [x] **CLAM-04**: `GetHealthAsync()` uses the same override-aware config path as `ScanFileAsync()`

### Status Endpoint

- [ ] **STAT-01**: `GET /healthz/status` returns JSON with .NET runtime metrics (memory working set, GC gen0/1/2 collections, thread pool stats, uptime)
- [ ] **STAT-02**: Endpoint is only registered when `STATUS_API_KEY` env var is configured (not mapped otherwise, matching OTEL pattern)
- [ ] **STAT-03**: Endpoint requires `X-Status-Api-Key` header matching `STATUS_API_KEY` env var; returns 401 on mismatch
- [ ] **STAT-04**: API key comparison uses `CryptographicOperations.FixedTimeEquals` (constant-time, matching existing PasswordHasher pattern)
- [ ] **STAT-05**: API key value is never written to logs at any log level

## Future Requirements

Deferred to v1.x after validation.

### UX Polish

- **UX-01**: Settings UI shows "(overridden by env var)" badge next to ClamAV host/port when env var is active
- **UX-02**: Status endpoint includes Blazor SignalR circuit count (approximate, via MeterListener)

### Extended Bootstrap

- **EXTBOOT-01**: Full configuration seeding via CLI (bot token, OpenAI key, etc.)
- **EXTBOOT-02**: `--bootstrap-owner --skip-totp` flag for fully headless environments

## Out of Scope

| Feature | Reason |
|---------|--------|
| DB health in status endpoint | Hosting provider monitors PostgreSQL externally |
| ClamAV health in status endpoint | Hosting provider monitors shared ClamAV externally |
| Bot connection state in status endpoint | Subscriber's responsibility, not hosting provider's concern |
| App-level monitoring hooks / billing | Hosting provider model, not platform operator |
| Distributed systems (Redis, RabbitMQ, S3) | Singleton architecture by design |
| OTEL/Prometheus changes | Already conditional on env vars, no work needed |
| mTLS for status endpoint | Overkill for API key gated endpoint behind K8s NetworkPolicy |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| BOOT-01 | Phase 10 | Pending |
| BOOT-02 | Phase 10 | Pending |
| BOOT-03 | Phase 10 | Pending |
| BOOT-04 | Phase 10 | Pending |
| BOOT-05 | Phase 10 | Pending |
| BOOT-06 | Phase 10 | Pending |
| BOOT-07 | Phase 10 | Pending |
| CLAM-01 | Phase 9 | Complete |
| CLAM-02 | Phase 9 | Complete |
| CLAM-03 | Phase 9 | Complete |
| CLAM-04 | Phase 9 | Complete |
| STAT-01 | Phase 11 | Pending |
| STAT-02 | Phase 11 | Pending |
| STAT-03 | Phase 11 | Pending |
| STAT-04 | Phase 11 | Pending |
| STAT-05 | Phase 11 | Pending |

**Coverage:**
- v1.2 requirements: 16 total
- Mapped to phases: 16
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-18*
*Last updated: 2026-03-18 after roadmap creation*
