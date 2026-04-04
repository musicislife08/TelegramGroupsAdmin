# Pitfalls Research

**Domain:** Adding CLI bootstrap, env var config override, and status endpoint to existing .NET 10 Blazor Server app (v1.2 SaaS Hosting Readiness)
**Researched:** 2026-03-18
**Confidence:** HIGH — All pitfalls grounded in observed codebase patterns, not hypothetical

---

## Critical Pitfalls

### Pitfall 1: Bootstrap Owner Creates Usable Account Without TOTP Secret

**What goes wrong:**
`CreateOwnerAccountAsync` (in `AuthService`) creates the user with `TotpEnabled: true` and `TotpSecret: null`. The login flow handles this combination by redirecting to TOTP setup. `--bootstrap-owner` will follow the same code path, but there is no UI to complete the setup. The bootstrapped account is in a permanently unusable state until a human visits the browser.

More critically: if bootstrap calls `userRepository.CreateAsync` directly (the same path as `CreateOwnerAccountAsync`), it replicates the exact record shape but may set `EmailVerified: false` if the author forgets the same conditional check that `CreateOwnerAccountAsync` does (`!await featureAvailability.IsEmailVerificationEnabledAsync()`). A headless bootstrap almost certainly has no email service configured, so `EmailVerified` must be forced `true` unconditionally.

**Why it happens:**
The existing `CreateOwnerAccountAsync` is designed for browser-based first-run. It defers TOTP setup to the UI flow. Bootstrap authors copy that pattern without realizing TOTP completion is mandatory to reach the dashboard.

**How to avoid:**
Bootstrap must write a complete, login-ready record:
- `TotpEnabled: false` — skip forced TOTP (the hosting provider will not have an authenticator device during bootstrap)
- `TotpSecret: null` — consistent with disabled TOTP
- `EmailVerified: true` — unconditionally (no email service in headless context)
- `Status: UserStatus.Active`

Bootstrap should NOT call `AuthService.RegisterAsync` — that path checks `IsFirstRunAsync` and could race. Bootstrap should call `IUserRepository.CreateAsync` directly after validating no users exist, then log an audit event via `IAuditService`.

**Warning signs:**
- Bootstrap completes with exit code 0 but login fails with "TOTP setup required"
- Integration test succeeds (user row created) but E2E test fails (cannot log in)

**Phase to address:** Phase handling `--bootstrap-owner` implementation

---

### Pitfall 2: Double-Bootstrap Silently Creates a Second Owner

**What goes wrong:**
If `--bootstrap-owner` is run twice (orchestrator restarts, misconfiguration), `GetUserCountAsync` returns 1, the idempotency check fails, and the command exits with an error — OR the developer skips the count check and a second Owner account is written. Either outcome is wrong: the first silently becomes non-idempotent (errors on restart), the second creates a security hole.

**Why it happens:**
The existing `--migrate-only` pattern just calls migrations (which are already idempotent). Bootstrap has side effects that must be made explicitly idempotent. Developers copy the `--migrate-only` exit pattern without adding the idempotency guard.

**How to avoid:**
Check `GetUserCountAsync` before writing. If count > 0, log "Owner account already exists, skipping bootstrap" and exit 0 — NOT exit 1. The orchestrator will retry on any non-zero exit. Exit 0 = success whether the account was created now or was already there.

Do NOT check by email — an attacker could pre-create an account with the bootstrap email. Check by count: if any user exists at all, bootstrap is a no-op.

**Warning signs:**
- Bootstrap exits with non-zero code when a user already exists, causing orchestrator to retry in a loop
- Two Owner accounts appear in the users table after a restart cycle

**Phase to address:** Phase handling `--bootstrap-owner` implementation

---

### Pitfall 3: Bootstrapped Password Arrives in Process Arguments (Visible in `ps`)

**What goes wrong:**
On Linux, `ps aux` and `/proc/[pid]/cmdline` expose all process arguments to any user who can list processes. If `--bootstrap-owner` accepts a password as `--password <value>`, the plaintext password is visible to all users on the host for the lifetime of the process.

**Why it happens:**
The existing `--backup` and `--restore` flags accept `--passphrase <value>` the same way. Bootstrap authors follow that pattern. For a passphrase that encrypts a backup blob it is a minor concern; for a login password that persists in the database, it is a permanent credential leak.

**How to avoid:**
Accept the password via environment variable (`TGA_BOOTSTRAP_PASSWORD`) not as a command-line argument. Fall back to stdin with a prompt if the env var is absent and stdin is a TTY. Never log the password or the hash. Use `IPasswordHasher.HashPassword` exactly as the normal registration path does — do not re-implement hashing.

**Warning signs:**
- `--password` or `--bootstrap-password` appears in the flag definition
- The password value appears in any log line

**Phase to address:** Phase handling `--bootstrap-owner` implementation

---

### Pitfall 4: ClamAV Env Var Override Is Read Once at Startup Instead of Per-Scan

**What goes wrong:**
`ClamAVScannerService.CreateClamClientAsync` calls `_configRepository.GetAsync` on every scan — this is intentional so UI config changes take effect without restart. If the env var override is implemented by injecting a cached value at startup (e.g., storing in `IOptions<ClamAVConfig>`), the env var wins at startup but the UI value wins on the next scan after a config save. Result: a user saves a different host in the UI and the env var is silently ignored.

Conversely, if the env var is checked on every DB read inside `ISystemConfigRepository`, the UI setting page will still display the DB value even though the runtime uses the env var — a confusing discrepancy.

**Why it happens:**
Env var overrides are commonly implemented via `IConfiguration` binding at startup. That pattern works for settings that are read once. Here the pattern is "read from DB each time" and the override must intercept that per-call read, not the startup configuration.

**How to avoid:**
Implement the override at the repository layer, not the DI layer. In `ISystemConfigRepository.GetAsync` (or its implementation), after fetching the DB value, apply env var overrides before returning. The `FileScanningConfig` returned to `ClamAVScannerService` already has the env var values applied. The UI Settings page reads the DB value directly (no override applied at display time) and should show a read-only notice when env vars are active.

Pattern:
```
DB value loaded → apply CLAMAV_HOST/CLAMAV_PORT env var overrides → return to service
```

**Warning signs:**
- ClamAV connects to the DB host after an env var is set (override not applied)
- ClamAV connects to the DB host after a UI save even with env var set (override applied at DI layer only at startup)
- Health check reports the DB host but the scanner uses the env var host — different sources of truth

**Phase to address:** Phase handling ClamAV env var override

---

### Pitfall 5: Health Check at `/healthz/status` Uses the DB-Sourced ClamAV Config Instead of the Runtime Config

**What goes wrong:**
`ClamAVScannerService.GetHealthAsync` calls `GetConfigAsync` internally — which now has env var overrides applied. But if the status endpoint calls `ClamAVScannerService.GetHealthAsync`, the host shown in the response will be the overridden value. If the status endpoint reads the config directly from the repository *without* going through the override layer, it reports the wrong host.

This is a consistency pitfall: the health check result must reflect the *actual runtime state* of the scanner, not the DB state.

**Why it happens:**
Status endpoint authors call `ISystemConfigRepository.GetAsync` for display because it seems straightforward. They do not realize `ClamAVScannerService` has its own `GetConfigAsync` helper that now applies overrides.

**How to avoid:**
The status endpoint must call `ClamAVScannerService.GetHealthAsync` (which uses the override-aware config path), not query the config repository directly. If connection info needs to be reported in the status JSON, it comes from the `FileScannerHealthResult` which already captures `Host` and `Port` from the runtime config.

**Warning signs:**
- Status endpoint reports `localhost:3310` while ClamAV actually connects to `clamav-shared:3310`
- Health check passes but status endpoint shows "ClamAV unreachable" because it used a different host

**Phase to address:** Phase handling `/healthz/status` implementation

---

### Pitfall 6: `STATUS_API_KEY` Compared With String Equality (Timing Attack)

**What goes wrong:**
`string key == expected` exits early on the first mismatched character. An attacker with sub-millisecond timing precision can enumerate the key one character at a time. This is a classic timing side-channel on API key validation.

**Why it happens:**
Developers use `==` for string comparison everywhere else in the application. API key validation looks identical to other string checks.

**How to avoid:**
Use `CryptographicOperations.FixedTimeEquals` on the UTF-8 byte representations of both strings. The application already uses this for password hashing (`VerifyPassword` in `PasswordHasher`), so the pattern is established. Do not use `string.CompareOrdinal`, `StringComparer.Ordinal`, or any string-based comparison — they are not constant-time.

```csharp
var keyBytes = Encoding.UTF8.GetBytes(providedKey);
var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
if (keyBytes.Length != expectedBytes.Length ||
    !CryptographicOperations.FixedTimeEquals(keyBytes, expectedBytes))
{
    return Results.StatusCode(401);
}
```

**Warning signs:**
- `==`, `Equals`, `string.Compare`, or `StringComparer` used in the API key check
- A helper that pads to equal length but then uses `==`

**Phase to address:** Phase handling `/healthz/status` implementation

---

### Pitfall 7: `STATUS_API_KEY` Logged at Startup or in Request Logs

**What goes wrong:**
The application logs configuration values at startup for diagnostics (e.g., "SEQ_URL configured: http://seq:5341"). If someone adds a similar "STATUS_API_KEY configured: <value>" log, the secret key appears in Seq, console output, and any log aggregation tool the hosting provider uses.

ASP.NET Core's default request logging also logs query strings. If any caller passes the key as a query parameter (`?key=abc`), it appears in access logs.

**Why it happens:**
Startup diagnostic logging is a helpful pattern. Developers log all configured env vars for observability. API keys look like configuration values.

**How to avoid:**
Log only whether the key is configured, not its value: "STATUS_API_KEY: configured" vs "STATUS_API_KEY: not configured (endpoint disabled)". Accept the key in the `X-Api-Key` header only — never as a query parameter. ASP.NET Core request logging does not log request headers by default, so the header approach is safe.

**Warning signs:**
- Any log statement that includes the `STATUS_API_KEY` value
- The endpoint accepting `?key=` query parameter
- Log redaction missing from startup configuration dump

**Phase to address:** Phase handling `/healthz/status` implementation

---

### Pitfall 8: Status Endpoint Disables Itself When `STATUS_API_KEY` Is Not Set Instead of Returning 401

**What goes wrong:**
A common pattern is "if the key is not configured, don't register the endpoint at all." This makes the endpoint undiscoverable in production when the key is absent — useful security theater. But it creates a subtle deployment bug: if the orchestrator polls `/healthz/status` before the env var is injected (e.g., a Kubernetes secret not yet mounted), the endpoint returns 404 instead of 401. The orchestrator may interpret a 404 as "endpoint removed" and alert incorrectly.

**Why it happens:**
Developers want to avoid serving an unprotected endpoint. Disabling the route seems safer than serving it without auth.

**How to avoid:**
Always register the route. When `STATUS_API_KEY` is not configured, return `503 Service Unavailable` with body `{"error": "Status endpoint not configured"}`. This gives the orchestrator a clear signal that the instance is not yet ready for monitoring (as opposed to 404 = endpoint does not exist).

**Warning signs:**
- `MapApiEndpoints` has a conditional block that skips registering the status route
- Status returns 404 during a startup window where the env var is not yet available

**Phase to address:** Phase handling `/healthz/status` implementation

---

### Pitfall 9: SignalR Circuit Count in Status Response Is Stale or Inaccurate

**What goes wrong:**
Blazor Server SignalR circuits are tracked in-memory by ASP.NET Core's circuit registry. There is no public API in .NET 10 to count active circuits. Developers either skip this metric (fine) or attempt to count it by injecting a custom `CircuitHandler` that increments/decrements a counter. The counter becomes inaccurate if the circuit handler's `OnCircuitOpenedAsync` fires without a matching `OnCircuitClosedAsync` (client hard-closes browser tab, network timeout, unhandled exception in circuit).

**Why it happens:**
The `CircuitHandler` approach is the documented way to hook Blazor Server lifecycle, but circuit close is not guaranteed on hard disconnects. The counter drifts upward over time, making the metric misleading.

**How to avoid:**
Do not report active circuit count as a precise metric. Either omit it, or report it with a note that it is a best-effort approximation. If circuit activity is important to the hosting provider, use the existing connection count from ASP.NET Core's server metrics (via OTEL) instead — those are accurate.

Alternatively, report the `SignalR` connection count from `IHubContext` if the signal-level count is acceptable (it counts transport connections, not logical Blazor circuits, but it is accurate).

**Warning signs:**
- Status response includes `active_circuits` that trends upward between restarts
- No `OnCircuitClosedAsync` decrement in the counter service
- Circuit count never reaches zero even when no browser tabs are open

**Phase to address:** Phase handling `/healthz/status` implementation

---

### Pitfall 10: GC Stats Collection Blocks the Status Response

**What goes wrong:**
`GC.GetGCMemoryInfo()` and `GC.CollectionCount()` are cheap. But if the status endpoint also calls `GC.Collect()` to force a collection before reporting (to get "current" stats), it introduces a full GC pause synchronously in the request handler. On a busy instance, this stalls all Blazor SignalR heartbeats for tens to hundreds of milliseconds.

**Why it happens:**
Documentation examples for memory reporting often include a `GC.Collect()` call to show "accurate" current heap usage. Developers copy this pattern.

**How to avoid:**
Never call `GC.Collect()` in a request handler. Use `GC.GetGCMemoryInfo()` and `Environment.WorkingSet` for memory stats — these are non-blocking reads of the last GC stats. The values may be slightly stale (since the last GC cycle) but are safe to call on-demand.

**Warning signs:**
- `GC.Collect()` in the status handler implementation
- Response latency spikes seen in logs correlating with status endpoint polling

**Phase to address:** Phase handling `/healthz/status` implementation

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Accept bootstrap password as CLI argument | Simpler code (follows `--passphrase` pattern) | Password visible in `ps aux`, process list, container logs | Never — the existing `--passphrase` pattern is acceptable for backup keys but not for persistent login credentials |
| Apply ClamAV env var override at DI startup (IOptions) | One-time binding, no per-call overhead | Runtime override silently lost after UI config save | Never — the per-scan DB read is the established pattern; override must be at that layer |
| String equality for API key | Simple, readable | Timing side-channel enables key enumeration | Never for secrets — use `CryptographicOperations.FixedTimeEquals` |
| Skip audit log for bootstrap owner creation | Fewer lines of code | No record of when/how the account was created, breaks audit trail | Never — audit log is the single source of truth for account creation events |
| Bootstrap calls `AuthService.RegisterAsync` | Reuses existing registration logic | Race condition with first-run detection; email service errors block bootstrap | Never — call `IUserRepository.CreateAsync` directly with a fully-constructed record |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| ClamAV env var override | Override applied in `IConfiguration` or `IOptions` binding (startup only) | Override applied inside `ISystemConfigRepository.GetAsync` after DB fetch (per-call) |
| Status endpoint + ClamAV health | Call config repo directly for host/port display | Call `ClamAVScannerService.GetHealthAsync` — it uses the override-aware config path |
| Bootstrap + TOTP | Copy `CreateOwnerAccountAsync` with `TotpEnabled: true` | Set `TotpEnabled: false` for headless bootstrap (no authenticator device available) |
| Bootstrap + email verification | Conditionally set `EmailVerified` based on feature flag | Force `EmailVerified: true` unconditionally (no email service in headless context) |
| STATUS_API_KEY + request logging | Pass key as query param (auto-logged by request middleware) | Accept key in `X-Api-Key` header only (not logged by default) |
| Bootstrap + existing users | Exit 1 when user count > 0 | Exit 0 with "already bootstrapped" log — orchestrator retries on non-zero exit |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| `GC.Collect()` in status handler | Response latency spikes; Blazor SignalR heartbeat misses | Use `GC.GetGCMemoryInfo()` without forcing collection | Every status poll (typically every 30s) |
| Status endpoint hitting DB for all metrics | DB query on every status poll (30s intervals), even when DB is the health check target | Cache stable values (version, start time) at startup; only live-query volatile values (DB connectivity) | At scale with many hosted instances polling simultaneously |
| PBKDF2 in bootstrap handler on main thread | Bootstrap takes 100–500ms per hash (expected) but blocks if not `await`ed or if called in a tight loop | The single hash in bootstrap is fine; never call `HashPassword` in a loop without async boundaries | N/A for single-user bootstrap |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Bootstrap password as CLI arg | Password visible in `ps aux`, container runtime logs, orchestrator logs | Accept via env var `TGA_BOOTSTRAP_PASSWORD` only |
| String equality on `STATUS_API_KEY` | Timing side-channel enables key enumeration | `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes |
| STATUS_API_KEY logged at startup | Key appears in Seq, console, log aggregators | Log "configured: yes/no" not the value |
| Status endpoint exposes internal hostnames, DB credentials, or connection strings | Information disclosure to anyone who obtains the key | Expose only: version, uptime, component health booleans, public config (ClamAV enabled/disabled), never connection strings or credentials |
| Bootstrap skips audit log | No forensic record of account creation; breaks compliance | Write `AuditEventType.UserRegistered` via `IAuditService` as the last step of bootstrap |
| Status endpoint returns 200 with `{"status":"degraded"}` | Kubernetes readiness/liveness probes use HTTP status codes, not body | Use HTTP 503 for degraded/unhealthy, 200 only for healthy — the status body is for the hosting provider's UI |

---

## "Looks Done But Isn't" Checklist

- [ ] **Bootstrap idempotency:** Owner created AND exit 0 on re-run when user already exists — verify `GetUserCountAsync` guard and exit code
- [ ] **Bootstrap TOTP state:** Bootstrapped user has `TotpEnabled: false` — verify by attempting login (should not prompt for TOTP setup)
- [ ] **Bootstrap email verification:** `EmailVerified: true` unconditionally — verify by attempting login (should not block on email verification)
- [ ] **Bootstrap audit log:** `audit_log` table has a `UserRegistered` entry after bootstrap — verify with direct DB query
- [ ] **ClamAV env var precedence:** Set `CLAMAV_HOST=test-override`, confirm `ClamAVScannerService` connects to `test-override`, confirm UI still shows DB value
- [ ] **ClamAV health check uses runtime config:** `GetHealthAsync` reports `test-override` host when env var is set, not `localhost`
- [ ] **Status key comparison:** Verify with a key where first character matches but rest differs — should return 401 consistently without timing variance
- [ ] **Status key not in logs:** Grep Seq/console output for `STATUS_API_KEY` value after startup — must not appear
- [ ] **Status endpoint active with no key:** When `STATUS_API_KEY` env var is absent, endpoint returns 503 not 404
- [ ] **GC stats are read-only:** No `GC.Collect()` in status handler — search the implementation

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Bootstrap wrote account with TotpEnabled=true (can't log in) | LOW | Direct DB update: `UPDATE users SET totp_enabled=false WHERE permission_level=2` — then re-attempt login |
| Double-bootstrap created two Owner accounts | LOW | Delete the duplicate in the Users UI (Owner can delete another Owner), or direct DB delete |
| ClamAV override applied at DI layer (UI changes ignored) | MEDIUM | Refactor override to repository layer; redeploy |
| STATUS_API_KEY leaked in logs | HIGH | Rotate the key in the orchestrator, redeploy; review log aggregator access logs for key usage |
| Status endpoint returns 200 for degraded state | MEDIUM | Update HTTP status code mapping; Kubernetes probes may have incorrectly marked instance as healthy during degraded period |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Bootstrap creates unusable TOTP state | `--bootstrap-owner` implementation | Integration test: bootstrap → login succeeds without TOTP prompt |
| Double-bootstrap is non-idempotent | `--bootstrap-owner` implementation | Run bootstrap twice; second run exits 0, user count is still 1 |
| Password in CLI args | `--bootstrap-owner` implementation | Review flag definitions; env var only for password input |
| ClamAV override at DI layer (not per-scan) | ClamAV env var override implementation | Set env var, save different host in UI, verify scanner uses env var host on next scan |
| Status endpoint uses DB config not runtime config | `/healthz/status` implementation | Set env var override, call status endpoint, verify reported host matches env var |
| Timing attack on STATUS_API_KEY | `/healthz/status` implementation | Code review: `CryptographicOperations.FixedTimeEquals` present |
| STATUS_API_KEY in logs | `/healthz/status` implementation | Grep logs for key value after startup |
| Status endpoint 404 when key not configured | `/healthz/status` implementation | Unset env var, call endpoint, verify 503 not 404 |
| Circuit count drift | `/healthz/status` implementation | Omit circuit count or document as approximation |
| GC.Collect() in status handler | `/healthz/status` implementation | Code review: no `GC.Collect()` in handler |

---

## Sources

- Codebase: `TelegramGroupsAdmin/Services/AuthService.cs` (CreateOwnerAccountAsync, first-run detection, TOTP state machine)
- Codebase: `TelegramGroupsAdmin/Services/Auth/PasswordHasher.cs` (PBKDF2 + CryptographicOperations.FixedTimeEquals pattern)
- Codebase: `TelegramGroupsAdmin/Services/Auth/TotpService.cs` (TotpEnabled=true + null secret = forced setup flow)
- Codebase: `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs` (per-scan DB config read pattern)
- Codebase: `TelegramGroupsAdmin/WebApplicationExtensions.cs` (health check endpoint registration pattern)
- Codebase: `TelegramGroupsAdmin/Program.cs` (existing CLI flag patterns: --migrate-only, --backup, --restore)
- Official: Microsoft Docs — `CryptographicOperations.FixedTimeEquals` (constant-time comparison)
- Official: ASP.NET Core — Blazor Server `CircuitHandler` lifecycle (OnCircuitOpenedAsync / OnCircuitClosedAsync)
- Official: .NET `GC.GetGCMemoryInfo()` — non-blocking, reads last GC cycle stats

---
*Pitfalls research for: v1.2 SaaS Hosting Readiness (CLI bootstrap, ClamAV env var override, status endpoint)*
*Researched: 2026-03-18*
