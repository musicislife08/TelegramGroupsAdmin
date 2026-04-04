# Feature Research

**Domain:** SaaS hosting readiness — headless bootstrap, env var config override, runtime status endpoint
**Researched:** 2026-03-18
**Confidence:** HIGH (brownfield, all three features have well-established patterns in the existing codebase)

---

## Feature Landscape

### Table Stakes (Users Expect These)

These are the three features the milestone exists to deliver. Each is a direct enabler of
managed/SaaS hosting. Missing any one = the hosting orchestrator cannot deploy TGA instances
without manual intervention.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| `--bootstrap-owner <email> <password>` CLI flag | Kubernetes init containers run one-shot commands before the main container starts. Hosting orchestrators cannot open a browser to `/register`. Headless bootstrapping is the standard pattern for "first run" in any containerised app. | LOW | Follows the exact pattern of existing `--migrate-only`, `--backup`, `--restore` flags in Program.cs. Reuses `AuthService.CreateOwnerAccountAsync` logic. Must run after `RunDatabaseMigrationsAsync()`, exit 0 on success, exit 1 if owner already exists or credentials are invalid. |
| `CLAMAV_HOST` / `CLAMAV_PORT` env var override | Shared ClamAV daemons are standard in multi-tenant hosting (one clamd container, many app containers). ClamAV host is infrastructure topology — it belongs in orchestrator env, not a per-instance DB config field. | LOW | Override only Host and Port (not Enabled/TimeoutSeconds — those remain app config). `ClamAVScannerService.CreateClamClientAsync()` currently reads from DB via `ISystemConfigRepository`. Add override check: if env var is set, use it; otherwise fall back to DB value. Precedence: env var wins. |
| `GET /healthz/status` JSON endpoint | Hosting providers need lightweight polling to assess instance health beyond binary alive/ready. A structured JSON status response (memory, GC, bot connection, DB, uptime) is the standard pattern for managed hosting dashboards. Must be gated behind an API key to prevent public exposure of runtime internals. | MEDIUM | New endpoint joining `/healthz/live` and `/healthz/ready`. Gated with `STATUS_API_KEY` env var (absent = endpoint returns 404, not 401, to avoid revealing its existence). Reads from `GC.GetTotalMemory()`, `Environment.TickCount64`, bot connection state (from `TelegramBotPollingHost` or equivalent health service), DB ping, and SignalR circuit count if available. Returns `application/json`. |

### Differentiators (Competitive Advantage)

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Status endpoint includes bot connection state | Most status endpoints only show process/infra metrics. Including Telegram bot polling status ("connected" / "disconnected" / "last_update_at") tells the hosting provider whether the core product function is working, not just whether the container is alive. | LOW | Requires reading state from `TelegramBotPollingHost` or a lightweight status tracker it updates. No new DB queries needed. |
| Bootstrap flag sets `EmailVerified = true` automatically | A headlessly bootstrapped owner has no inbox to click a verification link. The existing `CreateOwnerAccountAsync` already bypasses verification when email service is unconfigured (`EmailVerified: !await featureAvailability.IsEmailVerificationEnabledAsync()`). Bootstrap flag should force `EmailVerified = true` unconditionally — the orchestrator is trusted. | LOW | One-line behavioural difference vs the UI registration path. Prevents a bootstrap that silently leaves the owner unable to log in. |
| Env var override logs effective config at startup | When CLAMAV_HOST is set, log "ClamAV host overridden by environment variable: {Host}:{Port}" at Information level. Silent overrides cause support confusion. | LOW | Single log line in `ClamAVScannerService` or wherever the override is applied. |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| `--bootstrap-owner` that re-runs silently if owner exists | Seems safe for idempotent init containers | Hides misconfiguration. If an orchestrator passes wrong credentials on second run, it should fail loudly, not silently. A bootstrap that exits 0 when an owner already exists masks deploy errors. | Exit 1 with a clear error message: "Owner account already exists. Bootstrap skipped." Let the orchestrator decide whether to treat that as a warning. |
| Expose all DB config fields as env var overrides | Seems like "completeness" | Turns the DB-first configuration model into chaos. Each env var override is a hidden configuration layer that bypasses the Settings UI. Only infrastructure topology (ClamAV host/port) belongs in env vars. App config (API keys, enabled flags, timeouts) stays in DB. | Scope the env var override to exactly `CLAMAV_HOST` and `CLAMAV_PORT`. Document the precedence clearly. |
| Status endpoint with authentication via cookie/JWT | Seems more secure | Hosting providers poll status endpoints with a static API key, not user sessions. Cookie auth requires a browser or session management. JWT requires a token issuance flow. Both add friction for a simple polling use case. | Bearer token via `STATUS_API_KEY` env var checked in the endpoint handler. Simple header: `Authorization: Bearer <key>` or `X-Status-Api-Key: <key>`. |
| Status endpoint always-on (no key gate) | Simplifies deployment | Exposes runtime internals (memory, uptime, bot token presence) to anyone who knows the URL. Even in homelab use, this is a bad default. | Absent `STATUS_API_KEY` env var = endpoint not mapped (returns 404). Present = endpoint requires the key. Matches the OTEL conditional pattern already in Program.cs. |
| `--bootstrap-owner` creates user with TOTP pre-enrolled | Seems secure | Bootstrapped owner has no TOTP secret yet. Forcing `TotpEnabled = true` with no secret triggers the "admin enabled TOTP but user needs to set it up" flow — user is redirected to `/totp/setup` on first login. This is correct behaviour for the existing code path (`TotpEnabled=true, TotpSecret=null`). | Do not pre-enroll. Set `TotpEnabled = true`, `TotpSecret = null` exactly as the existing `CreateOwnerAccountAsync` does. First login triggers TOTP setup. |

---

## Feature Dependencies

```
--bootstrap-owner
    └──requires──> RunDatabaseMigrationsAsync() (migrations must run first, owner table must exist)
    └──reuses──>   AuthService.CreateOwnerAccountAsync() (existing logic, no duplication)
    └──reuses──>   IPasswordHasher.HashPassword() (existing)
    └──enhances──> IsFirstRunAsync() guard (must check: if owner exists, exit 1)

CLAMAV_HOST / CLAMAV_PORT env vars
    └──overrides──> ISystemConfigRepository.GetAsync() (DB read in ClamAVScannerService)
    └──scoped to──> ClamAVScannerService only (not a global config change)

GET /healthz/status
    └──requires──> STATUS_API_KEY env var (absent = endpoint not mapped, present = key checked per-request)
    └──reads──>    GC.GetTotalMemory(), Environment.TickCount64 (in-process, no DB)
    └──reads──>    NpgsqlDataSource ping (reuses existing readiness check pattern)
    └──reads──>    TelegramBotPollingHost connection state (new: requires lightweight state tracking)
    └──enhances──> /healthz/live and /healthz/ready (adds detail layer, does not replace them)
```

### Dependency Notes

- `--bootstrap-owner` requires `RunDatabaseMigrationsAsync()` first: the migrations must have run so the users table exists. In Program.cs the flag check must come after `app.RunDatabaseMigrationsAsync()` — the same position as `--migrate-only`, `--backup`, `--restore`.
- `GET /healthz/status` requires Telegram bot polling state: `TelegramBotPollingHost` (or a status tracker it updates) must expose last-connected-at or connection status. This is the only new interface surface. If bot state is not available, the endpoint should return `"bot": "unknown"` rather than fail.
- `CLAMAV_HOST` / `CLAMAV_PORT` override is scoped to `ClamAVScannerService`: it does not affect the Settings UI display (which still reads from DB). The UI should show the effective host/port, not the DB value, when an override is active — but this is a UX polish item, not a correctness requirement.

---

## MVP Definition

### Launch With (v1.2)

All three features are the milestone. Each is independently valuable and independently testable.

- [x] `--bootstrap-owner <email> <password>` — required for Kubernetes init container pattern; SaaS orchestrator cannot work without it
- [x] `CLAMAV_HOST` / `CLAMAV_PORT` env var override — required for shared ClamAV daemon; without it each TGA instance needs its own clamd or DB must be pre-seeded with correct host per-instance
- [x] `GET /healthz/status` gated by `STATUS_API_KEY` — required for hosting provider monitoring dashboard; `/healthz/ready` only tells alive/dead, not what's wrong

### Add After Validation (v1.x)

- [ ] Settings UI shows "(overridden by env var)" badge next to ClamAV host/port — useful UX but not a correctness requirement; add when operators report confusion
- [ ] `/healthz/status` SignalR circuit count — requires Blazor Server circuit tracking; useful but non-trivial; defer until circuit count is needed for capacity planning
- [ ] Bootstrap flag `--bootstrap-owner` with `--skip-totp` to create owner with TOTP disabled — only if SaaS orchestrator needs to support Blazor-less headless environments

### Future Consideration (v2+)

- [ ] Full configuration seeding via CLI (bot token, OpenAI key, etc.) — useful for fully headless first-run, but increases attack surface and complexity; defer until SaaS orchestrator actually needs it
- [ ] Status endpoint authenticated via mTLS — relevant for zero-trust networks; over-engineered for current homelab/SaaS use case

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| `--bootstrap-owner` | HIGH (SaaS orchestrator blocker) | LOW (follows existing CLI flag pattern exactly) | P1 |
| `CLAMAV_HOST` / `CLAMAV_PORT` env vars | HIGH (shared daemon is standard SaaS topology) | LOW (override in one service, one read path) | P1 |
| `GET /healthz/status` | HIGH (hosting provider needs more than alive/dead) | MEDIUM (new endpoint + bot state surface) | P1 |
| Status endpoint bot connection state | MEDIUM (nice diagnostic signal) | LOW (read from existing host, add state tracking) | P2 |
| ClamAV override startup log | MEDIUM (prevents silent misconfiguration) | LOW (one log line) | P1 (do it with the override) |
| Settings UI env var badge | LOW (UX polish) | LOW | P3 |

---

## Sources

- Codebase: `/TelegramGroupsAdmin/Program.cs` — existing CLI flag patterns (`--migrate-only`, `--backup`, `--restore`), health check wiring, OTEL conditional mapping
- Codebase: `/TelegramGroupsAdmin/Services/AuthService.cs` — `IsFirstRunAsync`, `CreateOwnerAccountAsync`, TOTP and email verification logic
- Codebase: `/TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs` — DB config read path, `CreateClamClientAsync`
- Codebase: `/TelegramGroupsAdmin/WebApplicationExtensions.cs` — health endpoint wiring, `MapApiEndpoints`
- Codebase: `/TelegramGroupsAdmin.Configuration/Models/ClamAVConfig.cs` — Host, Port, Enabled, TimeoutSeconds fields
- `.planning/PROJECT.md` — milestone scope, key decisions, constraints, out-of-scope items

---

*Feature research for: TelegramGroupsAdmin v1.2 SaaS Hosting Readiness*
*Researched: 2026-03-18*
