# Project Research Summary

**Project:** TelegramGroupsAdmin v1.2 SaaS Hosting Readiness
**Domain:** Brownfield .NET 10 Blazor Server — headless bootstrap, infrastructure env var override, runtime status endpoint
**Researched:** 2026-03-18
**Confidence:** HIGH

## Executive Summary

This is a brownfield milestone adding three independent SaaS hosting readiness capabilities to the existing TelegramGroupsAdmin stack (.NET 10, Blazor Server, EF Core 10, PostgreSQL 18, Quartz.NET). No new packages, no new projects, and no architectural pivots are required. Each feature plugs into an established codebase pattern: the `--bootstrap-owner` CLI flag follows the `--migrate-only`/`--backup`/`--restore` shape already in `Program.cs`; the ClamAV env var override is a two-line interception in one existing method; and the `/healthz/status` endpoint extends the existing minimal API endpoint structure in `Endpoints/`. The recommended implementation order is ClamAV override first (zero regression risk), bootstrap flag second, status endpoint third (most API design decisions).

The primary technical risk is not architectural — it is correctness in edge cases that are easy to miss when the happy path looks clean. The bootstrap flag has three specific failure modes: a bootstrapped account that cannot log in (TOTP state), a non-idempotent command that breaks orchestrator restart loops (exit code semantics), and a password exposed in `ps aux` (credential handling). The status endpoint has two security requirements that look like standard string operations but are not: constant-time API key comparison and suppression of the key value from all log output. These pitfalls are fully documented and preventable with targeted implementation decisions.

The confidence across all four research areas is HIGH because every finding is grounded in direct codebase inspection of the relevant source files, not inferred from documentation alone. The three features do not interact with each other at runtime and can be implemented and tested independently. All validation can be done via `dotnet run --migrate-only` (build check) and integration tests; no manual runtime testing is required for build correctness.

## Key Findings

### Recommended Stack

No new technologies are introduced in this milestone. The entire implementation uses BCL APIs (`Environment.GetEnvironmentVariable`, `GC.GetGCMemoryInfo`, `System.Diagnostics.Metrics.MeterListener`, `CryptographicOperations.FixedTimeEquals`) and existing registered services (`IAuthService`, `IUserRepository`, `IAuditService`, `ISystemConfigRepository`). No changes to `Directory.Packages.props` are needed.

**Core technologies (additions only):**
- `System.Diagnostics.Metrics.MeterListener` (BCL, .NET 6+): reads the `aspnetcore.components.circuit.active` OTel meter for Blazor circuit count without requiring the full OTel pipeline — no new package
- `CryptographicOperations.FixedTimeEquals` (BCL): constant-time API key comparison — already used in `PasswordHasher`, pattern is established
- `Environment.GetEnvironmentVariable` (BCL): ClamAV host/port override and `STATUS_API_KEY` / `TGA_BOOTSTRAP_PASSWORD` reading — no `IConfiguration` injection needed

### Expected Features

**Must have (table stakes — v1.2 scope):**
- `--bootstrap-owner <email>` with password via `TGA_BOOTSTRAP_PASSWORD` env var — required for Kubernetes init container pattern; SaaS orchestrator cannot function without headless account creation
- `CLAMAV_HOST` / `CLAMAV_PORT` env var override — required for shared ClamAV daemon topology; without it each instance needs its own clamd or the DB must be pre-seeded per-instance
- `GET /healthz/status` gated by `STATUS_API_KEY` — required for hosting provider monitoring; `/healthz/ready` only reports alive/dead, not component-level health

**Should have (differentiators, implement alongside the must-haves):**
- Bootstrap forces `EmailVerified: true` unconditionally — headless bootstrap has no email service; a bootstrapped account that cannot log in due to email verification is a silent failure
- ClamAV env var override logs effective config at INFO level — silent overrides cause support confusion; one log line prevents it
- Status endpoint bot connection state — reports Telegram polling status, not just process/infra metrics

**Defer (v1.x after validation):**
- Settings UI shows "(overridden by env var)" badge next to ClamAV host/port — UX polish, not a correctness requirement
- `/healthz/status` Blazor circuit count — non-trivial to report accurately; defer until capacity planning requires it
- `--bootstrap-owner --skip-totp` flag — only needed if SaaS orchestrator requires Blazor-less headless environments

**Defer (v2+):**
- Full configuration seeding via CLI (bot token, OpenAI key, etc.)
- Status endpoint authenticated via mTLS

### Architecture Approach

All three features are surgical modifications to existing files plus one new file (`StatusEndpoints.cs`). The startup sequence in `Program.cs` already establishes the early-exit CLI flag pattern; `--bootstrap-owner` slots in after `--restore` and before ML training. `ClamAVScannerService.CreateClamClientAsync()` is the single read site for ClamAV connection config; the env var check intercepts at that point without touching the DB model or the UI display path. The status endpoint follows the existing minimal API pattern in `Endpoints/AuthEndpoints.cs` and is wired via a `MapStatusEndpoints()` extension call in `WebApplicationExtensions.MapApiEndpoints()`.

**Modified files:**
1. `Program.cs` — add `--bootstrap-owner` block after `--restore`, before ML training
2. `ClamAVScannerService.cs` — intercept `CreateClamClientAsync()` with env var override
3. `WebApplicationExtensions.cs` — add `app.MapStatusEndpoints()` call

**New files:**
1. `Endpoints/StatusEndpoints.cs` — `GET /healthz/status` with `X-Status-Api-Key` gate

### Critical Pitfalls

1. **Bootstrap creates unusable TOTP state** — `CreateOwnerAccountAsync` sets `TotpEnabled: true`/`TotpSecret: null`, which blocks login with a TOTP setup redirect. Bootstrap must set `TotpEnabled: false`. Do not call `AuthService.RegisterAsync` — call `IUserRepository.CreateAsync` directly with a fully-constructed, login-ready record. Follow with `IAuditService.LogEventAsync` for the audit trail.

2. **Bootstrap is non-idempotent on exit code** — If `GetUserCountAsync` returns > 0 and bootstrap exits with code 1, the orchestrator retries in an infinite loop. Exit 0 with a "already bootstrapped" log message when any user already exists. The guard is on count (any user exists), not on matching email, to prevent pre-seeded account attacks.

3. **Bootstrap password visible in `ps aux`** — Never accept the password as a CLI argument. Read from `TGA_BOOTSTRAP_PASSWORD` env var. Existing `--passphrase` CLI arg for backups is an acceptable trade-off for that use case; it is not acceptable for a persistent login credential.

4. **ClamAV env var override applied at DI startup instead of per-scan** — `ClamAVScannerService` reads DB config on every scan intentionally so UI changes take effect without restart. The env var override must be applied at `CreateClamClientAsync()` call time, not cached at startup. Implement at the service method level, not via `IOptions` or DI binding.

5. **STATUS_API_KEY compared with string equality** — `string ==` exits early on first mismatched character, enabling timing-based key enumeration. Use `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes. The pattern is already in `PasswordHasher.VerifyPassword`; copy it exactly.

## Implications for Roadmap

Based on research, the three features are independent and can be implemented in any order. The recommended order is driven by risk and build verification confidence, not dependency.

### Phase 1: ClamAV Environment Variable Override

**Rationale:** Single method modification with zero regression risk. `CreateClamClientAsync()` is a two-line change. Running the ContentDetection test suite after this change validates correctness before touching any other file. Establishing this change first also ensures the status endpoint (Phase 3) can call `ClamAVScannerService.GetHealthAsync()` and get the override-aware response without rework.

**Delivers:** `CLAMAV_HOST` and `CLAMAV_PORT` env vars override DB-stored ClamAV host/port at scan time. INFO log when override is active. `GetHealthAsync()` automatically uses override-aware config.

**Addresses:** Shared ClamAV daemon topology requirement; prevents per-instance DB pre-seeding.

**Avoids:** ClamAV override at DI layer pitfall (Pitfall 4); status endpoint using stale DB config (Pitfall 5).

### Phase 2: --bootstrap-owner CLI Flag

**Rationale:** More implementation decisions than Phase 1, but all patterns are established. Ordering this second means Phase 1 is already locked and tested. The bootstrap must: call `IUserRepository.CreateAsync` directly (not `RegisterAsync`), set `TotpEnabled: false` and `EmailVerified: true` unconditionally, read password from `TGA_BOOTSTRAP_PASSWORD` env var, exit 0 on count > 0 (idempotency), and write an audit log entry. Each of these is a distinct decision that must be made correctly.

**Delivers:** `docker run ... --bootstrap-owner admin@example.com` creates a fully login-ready Owner account without browser access. Second run exits 0 without creating a duplicate account. Password never appears in process arguments or logs.

**Addresses:** Kubernetes init container pattern; SaaS orchestrator cannot deploy without this.

**Avoids:** Unusable TOTP state (Pitfall 1); non-idempotent exit code (Pitfall 2); password in CLI args (Pitfall 3); missing audit log (Tech Debt table in PITFALLS.md).

### Phase 3: GET /healthz/status Endpoint

**Rationale:** Most design decisions of the three features; build last so the ClamAV override (Phase 1) is already in place and the health check can call through to the runtime-correct config path. New file (`StatusEndpoints.cs`) avoids merge conflicts with Phases 1 and 2. The response schema must be agreed before implementation.

**Delivers:** `GET /healthz/status` returns flat JSON with: status, uptime, memory metrics (BCL, no `GC.Collect()`), GC stats, thread pool metrics, DB health (reuses existing postgresql health check tagged "ready"), ClamAV health (via `GetHealthAsync()`), bot connection state. Returns 503 when `STATUS_API_KEY` not configured; 401 on key mismatch (constant-time comparison); 200 on success.

**Addresses:** Hosting provider monitoring dashboard requirement; component-level health beyond alive/dead.

**Avoids:** Timing attack on API key (Pitfall 6); API key in logs (Pitfall 7); 404 when key unconfigured (Pitfall 8); circuit count drift (Pitfall 9); `GC.Collect()` in handler (Pitfall 10).

### Phase Ordering Rationale

- Phase 1 before Phase 3: the status endpoint must call `ClamAVScannerService.GetHealthAsync()` to get override-aware ClamAV health; doing Phase 1 first means Phase 3 gets this for free
- Phase 2 is self-contained and has no ordering dependency on Phases 1 or 3; second position gives it a clean build baseline
- All three phases touch different files (no merge conflicts between phases)
- Each phase can be independently committed, PR'd, and tested

### Research Flags

Phases with well-documented patterns (standard implementation, no deeper research needed):
- **Phase 1 (ClamAV override):** Single method, BCL env var read, established override pattern in codebase
- **Phase 2 (Bootstrap flag):** Established CLI flag pattern in `Program.cs`; all required services already registered and tested

Phases that may benefit from schema validation before implementation:
- **Phase 3 (Status endpoint):** The response JSON schema should be agreed before coding — the hosting provider's monitoring dashboard will parse it. Confirm which fields are required vs optional before writing the handler. No external API research needed, but internal alignment on schema is recommended.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All three features use BCL and existing registered services; zero new packages; confirmed via codebase inspection of the exact files that will change |
| Features | HIGH | All three features derived from direct milestone scope; no ambiguity about what is in/out; anti-features clearly documented with rationale |
| Architecture | HIGH | Every integration point confirmed by reading actual source: `Program.cs` startup sequence, `AuthService` private/public boundary, `ClamAVScannerService.CreateClamClientAsync`, `WebApplicationExtensions.MapApiEndpoints` |
| Pitfalls | HIGH | All 10 pitfalls grounded in observed codebase patterns (TOTP state machine, password hasher, existing health check registration); none hypothetical |

**Overall confidence:** HIGH

### Gaps to Address

- **Status endpoint response schema:** The exact fields and their names are not pinned in research. Before implementing Phase 3, agree on the schema with the user. Suggested minimum: `status`, `uptime_seconds`, `memory_working_set_mb`, `db`, `clamav`, `bot`, `gc_gen0`/`gen1`/`gen2`. The circuit count field should be omitted or marked as approximate.
- **Bot connection state surface:** The status endpoint needs to read Telegram bot polling status. Research identifies `TelegramBotPollingHost` as the source but does not confirm the exact property or method to call. This needs a quick inspection of `TelegramBotPollingHost` before Phase 3 implementation. If state is not easily accessible, the endpoint returns `"bot": "unknown"` rather than failing.
- **`IUserRepository.CreateAsync` signature for bootstrap:** Research recommends calling `IUserRepository.CreateAsync` directly (not `RegisterAsync`) for the bootstrap path. The exact parameters and expected model shape should be verified against the repository interface before Phase 2 implementation. The `CreateOwnerAccountAsync` private method in `AuthService` is the reference implementation to match.

## Sources

### Primary (HIGH confidence)
- Codebase: `TelegramGroupsAdmin/Program.cs` — CLI flag pattern, startup sequence, health check wiring
- Codebase: `TelegramGroupsAdmin/Services/AuthService.cs` — `IsFirstRunAsync`, `CreateOwnerAccountAsync`, TOTP/email verification state
- Codebase: `TelegramGroupsAdmin/Services/Auth/PasswordHasher.cs` — `CryptographicOperations.FixedTimeEquals` pattern
- Codebase: `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs` — per-scan DB config read, `CreateClamClientAsync`
- Codebase: `TelegramGroupsAdmin/WebApplicationExtensions.cs` — health endpoint registration, `MapApiEndpoints`
- Codebase: `TelegramGroupsAdmin/Endpoints/AuthEndpoints.cs` — existing endpoint structure pattern
- Codebase: `TelegramGroupsAdmin.Configuration/Models/ClamAVConfig.cs` — Host, Port, Enabled, TimeoutSeconds fields
- [.NET runtime built-in metrics (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-runtime) — BCL APIs for GC, thread pool, working set
- [ASP.NET Core built-in metrics (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/built-in?view=aspnetcore-10.0) — `aspnetcore.components.circuit.active` meter name confirmed
- Official: Microsoft Docs — `CryptographicOperations.FixedTimeEquals` (constant-time comparison)
- Official: ASP.NET Core — Blazor Server `CircuitHandler` lifecycle

### Secondary (MEDIUM confidence)
- `.planning/PROJECT.md` — milestone scope, key decisions, out-of-scope items

---
*Research completed: 2026-03-18*
*Ready for roadmap: yes*
