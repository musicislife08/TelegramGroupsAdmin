# Architecture Research

**Domain:** SaaS Hosting Readiness — CLI Bootstrap, ClamAV Env Var Override, Runtime Status Endpoint
**Researched:** 2026-03-18
**Confidence:** HIGH (derived from direct codebase inspection)

## Standard Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                   Program.cs Startup Sequence               │
├─────────────────────────────────────────────────────────────┤
│  builder.Build()                                            │
│       ↓                                                     │
│  RunDatabaseMigrationsAsync()                               │
│       ↓                                                     │
│  Serilog InitializeAsync()                                  │
│       ↓                                                     │
│  --migrate-only → EXIT 0      ← (existing)                 │
│  --backup       → EXIT 0      ← (existing)                 │
│  --restore      → EXIT 0      ← (existing)                 │
│  --bootstrap-owner → EXIT 0   ← (NEW — fits here)          │
│       ↓                                                     │
│  ML Training (skippable via SKIP_ML_TRAINING)               │
│       ↓                                                     │
│  ConfigurePipeline()                                        │
│  MapApiEndpoints()            ← /healthz/live, /healthz/ready│
│  MapPrometheusScrapingEndpoint() (optional)                 │
│  app.Run()                    ← /healthz/status (NEW)      │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│               ContentDetection Layer                         │
├─────────────────────────────────────────────────────────────┤
│  ClamAVScannerService                                       │
│    CreateClamClientAsync()                                  │
│      → GetConfigAsync()   ← reads ISystemConfigRepository  │
│           ↓                                                 │
│      config.Tier1.ClamAV.Host / .Port   ← (DB values)     │
│           ↓                                                 │
│      NEW: check env vars CLAMAV_HOST / CLAMAV_PORT first    │
│      → env var wins over DB if set                          │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                  Endpoints/ (Minimal API)                    │
├─────────────────────────────────────────────────────────────┤
│  /healthz/live     (existing — AllowAnonymous)              │
│  /healthz/ready    (existing — AllowAnonymous)              │
│  /api/auth/*       (existing)                               │
│  /verify-email     (existing)                               │
│  /healthz/status   (NEW — gated by STATUS_API_KEY)          │
└─────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Location |
|-----------|----------------|----------|
| Program.cs | CLI flag dispatch, early-exit pattern | TelegramGroupsAdmin/Program.cs |
| AuthService | `IsFirstRunAsync()`, `CreateOwnerAccountAsync()` (private) | TelegramGroupsAdmin/Services/AuthService.cs |
| IAuthService | Public interface — does NOT expose `CreateOwnerAccountAsync` | TelegramGroupsAdmin/Services/IAuthService.cs |
| ClamAVScannerService | Reads DB config per scan via `ISystemConfigRepository` | TelegramGroupsAdmin.ContentDetection/Services/ |
| WebApplicationExtensions | `MapApiEndpoints()` — all HTTP endpoint registration | TelegramGroupsAdmin/WebApplicationExtensions.cs |
| StatusEndpoints (NEW) | `/healthz/status` JSON response, API key middleware | TelegramGroupsAdmin/Endpoints/StatusEndpoints.cs |

## Recommended Project Structure

```
TelegramGroupsAdmin/
├── Program.cs                          # ADD --bootstrap-owner block after --restore block
├── WebApplicationExtensions.cs         # ADD app.MapStatusEndpoints() call in MapApiEndpoints()
├── ServiceCollectionExtensions.cs      # No changes needed
├── Endpoints/
│   ├── AuthEndpoints.cs                # Existing
│   ├── EmailVerificationEndpoints.cs   # Existing
│   └── StatusEndpoints.cs             # NEW — /healthz/status endpoint

TelegramGroupsAdmin.ContentDetection/
└── Services/
    └── ClamAVScannerService.cs         # MODIFY CreateClamClientAsync() for env var override
```

No new projects needed. All three features are small enough to live in existing projects.

## Architectural Patterns

### Pattern 1: Early-Exit CLI Flag

**What:** After `app.Build()` and `RunDatabaseMigrationsAsync()`, check `args` array sequentially. If the flag is present, execute the operation using a scoped DI resolution, log result, then `Environment.Exit(0)`.

**When to use:** Any headless operation that needs the DI container and database but should not start the HTTP server.

**Trade-offs:** Simple and consistent. Order matters — `--bootstrap-owner` must go after `--restore` (restore wipes the DB, bootstrap would then immediately recreate the owner, which is the correct sequence if someone passes both).

**Example (existing pattern to follow):**
```csharp
// After --restore block in Program.cs
if (args.Contains("--bootstrap-owner"))
{
    var email = args.SkipWhile(a => a != "--bootstrap-owner").Skip(1).FirstOrDefault();
    var password = /* parse --password arg */;

    using var scope = app.Services.CreateScope();
    var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

    var isFirst = await authService.IsFirstRunAsync();
    if (!isFirst)
    {
        app.Logger.LogError("--bootstrap-owner: users already exist, skipping owner creation");
        Environment.Exit(1);
    }

    var result = await authService.RegisterAsync(email, password, inviteToken: null);
    // RegisterAsync → IsFirstRunAsync() → CreateOwnerAccountAsync() internally
    Environment.Exit(result.Success ? 0 : 1);
}
```

Key constraint: `CreateOwnerAccountAsync` is `private` on `AuthService`. The correct reuse path is `RegisterAsync(email, password, inviteToken: null)` which internally calls `IsFirstRunAsync()` and routes to `CreateOwnerAccountAsync`. No interface change required.

### Pattern 2: Env Var Override at Read Time

**What:** In `ClamAVScannerService.CreateClamClientAsync()`, after reading DB config, check environment variables and substitute values if present. The DB config object is immutable at the read site — create a new `ClamClient` with the override values rather than mutating the config.

**When to use:** Infrastructure-level settings (host/port) where the deployment environment controls the value, but the app's UI still shows and edits the DB value as the default.

**Trade-offs:** Env var wins silently. This is intentional — the SaaS orchestrator owns the daemon connection. Log a one-time message at INFO level when an override is active to aid debugging.

**Example:**
```csharp
private async Task<ClamClient> CreateClamClientAsync(CancellationToken cancellationToken = default)
{
    var config = await GetConfigAsync(cancellationToken);
    var host = Environment.GetEnvironmentVariable("CLAMAV_HOST") ?? config.Tier1.ClamAV.Host;
    var portStr = Environment.GetEnvironmentVariable("CLAMAV_PORT");
    var port = portStr != null && int.TryParse(portStr, out var p) ? p : config.Tier1.ClamAV.Port;
    return new ClamClient(host, port);
}
```

The `GetHealthAsync()` method on `ClamAVScannerService` already uses `CreateClamClientAsync()`, so it picks up the override automatically.

### Pattern 3: API-Key-Gated Minimal API Endpoint

**What:** A new `StatusEndpoints.cs` in the `Endpoints/` folder registers `GET /healthz/status`. The endpoint reads `STATUS_API_KEY` from `IConfiguration` (environment variable). If the header `X-Status-Api-Key` does not match, return 401. If set to empty/unset, return 401 unconditionally (fail-safe — never expose unauthenticated).

**When to use:** Any endpoint that exposes internal runtime state to an external orchestrator but must not be reachable without credentials.

**Trade-offs:** Simpler than cookie auth or JWT. The SaaS orchestrator polls this endpoint via HTTP; the API key is injected as an env var. No database dependency for the key lookup (stays fast even if DB is overloaded).

**Example structure:**
```csharp
public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/healthz/status", async (
            HttpContext httpContext,
            IConfiguration configuration,
            IHealthCheckService healthCheckService,
            CancellationToken cancellationToken) =>
        {
            var expectedKey = configuration["STATUS_API_KEY"];
            if (string.IsNullOrEmpty(expectedKey))
                return Results.Unauthorized();

            var providedKey = httpContext.Request.Headers["X-Status-Api-Key"].ToString();
            if (providedKey != expectedKey)
                return Results.Unauthorized();

            // Gather runtime metrics and return JSON
            var report = await healthCheckService.CheckHealthAsync(
                r => r.Tags.Contains("ready"), cancellationToken);

            return Results.Json(BuildStatusResponse(report));
        }).AllowAnonymous(); // Auth handled manually above (no cookie required)

        return endpoints;
    }
}
```

## Data Flow

### --bootstrap-owner Flow

```
docker run ... --bootstrap-owner admin@example.com --password secret
    ↓
Program.cs: args.Contains("--bootstrap-owner")
    ↓
app.Services.CreateScope()
    ↓
IAuthService.RegisterAsync(email, password, inviteToken: null)
    ↓
AuthService.IsFirstRunAsync() → IUserRepository.GetUserCountAsync() → DB
    ↓ (count == 0)
AuthService.CreateOwnerAccountAsync()
    ↓
IUserRepository.CreateAsync() → DB
IPasswordHasher.HashPassword() (sync)
IAuditService.LogEventAsync() → DB
    ↓
RegisterResult(Success: true)
    ↓
Environment.Exit(0)
```

### CLAMAV_HOST/CLAMAV_PORT Override Flow

```
ClamAVScannerService.ScanFileAsync()
    ↓
CreateClamClientAsync()
    ↓
GetConfigAsync() → ISystemConfigRepository → DB
    ↓
Check Environment.GetEnvironmentVariable("CLAMAV_HOST")
    ↓ override present
new ClamClient(envHost, envPort)   ← env wins
    ↓ no override
new ClamClient(config.Host, config.Port)  ← DB value
    ↓
nClam TCP connection to clamd daemon
```

### /healthz/status Request Flow

```
GET /healthz/status
    + Header: X-Status-Api-Key: <key>
    ↓
StatusEndpoints handler
    ↓
IConfiguration["STATUS_API_KEY"] — env var lookup (in-memory, no DB)
    ↓ key mismatch
401 Unauthorized
    ↓ key matches
IHealthCheckService.CheckHealthAsync(tag: "ready")
    ↓
NpgSql health check → DB ping
    ↓
GC.GetTotalMemory(), Environment.ProcessorCount (runtime APIs, no extra deps)
    ↓
200 JSON: { "status": "healthy", "db": "ok", "uptime_seconds": ..., "memory_mb": ... }
```

## Integration Points

### New vs Modified

| Component | Action | What Changes |
|-----------|--------|--------------|
| `Program.cs` | MODIFY | Add `--bootstrap-owner` block after `--restore` block, before ML training |
| `ClamAVScannerService.cs` | MODIFY | `CreateClamClientAsync()` checks `CLAMAV_HOST`/`CLAMAV_PORT` env vars |
| `Endpoints/StatusEndpoints.cs` | NEW | `GET /healthz/status` with `X-Status-Api-Key` gate |
| `WebApplicationExtensions.cs` | MODIFY | `MapApiEndpoints()` calls `app.MapStatusEndpoints()` |
| `IAuthService.cs` | NO CHANGE | `RegisterAsync` is the correct reuse point; no new interface method needed |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Program.cs → AuthService | Scoped DI resolution via `app.Services.CreateScope()` | Same pattern as --backup/--restore use for BackupService |
| ClamAVScannerService → env | `Environment.GetEnvironmentVariable()` directly in service | No IConfiguration injection needed; env vars are global |
| StatusEndpoints → IConfiguration | Constructor injection via minimal API parameter | `STATUS_API_KEY` read at request time, not cached |
| StatusEndpoints → IHealthCheckService | Built-in ASP.NET Core service, already registered via `AddHealthChecks()` | Can reuse the existing postgresql check tagged "ready" |

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| ClamAV daemon | TCP via nClam (existing) | `CLAMAV_HOST`/`CLAMAV_PORT` override the DB-stored host/port |
| SaaS orchestrator | HTTP GET `/healthz/status` | Polls periodically; authenticates with `STATUS_API_KEY` env var |
| PostgreSQL | Health check (existing) | Status endpoint reuses the existing `postgresql` health check tagged "ready" |

## Build Order

The three features are independent and have no dependencies on each other. The recommended order reflects risk, not coupling:

1. **CLAMAV_HOST/CLAMAV_PORT override** — Single method change in `ClamAVScannerService.CreateClamClientAsync()`. Zero risk of regression; adds two-line env var check before existing code. Build and test first to confirm no ContentDetection test breakage.

2. **--bootstrap-owner CLI flag** — Block in `Program.cs` + env-var argument parsing. Uses existing `IAuthService.RegisterAsync` with no interface changes. Verify that `IsFirstRunAsync()` path is exercised (count==0 precondition must hold; failure path must log clearly and exit non-zero).

3. **`/healthz/status` endpoint** — New file `StatusEndpoints.cs` + one call added to `MapApiEndpoints()`. Depends on understanding what runtime metrics to expose; requires the most API design decisions. Build last to avoid back-and-forth on the response schema.

## Anti-Patterns

### Anti-Pattern 1: Exposing `CreateOwnerAccountAsync` on the Interface

**What people do:** Add `CreateOwnerAccountAsync(string email, string password)` to `IAuthService` to make the CLI block "cleaner."

**Why it's wrong:** `CreateOwnerAccountAsync` is deliberately private because it bypasses invite validation, TOTP setup, and email verification. Exposing it widens the attack surface and tempts callers (tests, future code) to call it outside the first-run context. `RegisterAsync(email, password, null)` already calls it internally after the first-run guard.

**Do this instead:** Call `authService.RegisterAsync(email, password, inviteToken: null)` from the CLI block. The method already handles the first-run branch correctly.

### Anti-Pattern 2: Reading STATUS_API_KEY at App Startup and Caching It

**What people do:** Read `configuration["STATUS_API_KEY"]` in `MapStatusEndpoints()` and close over the string value.

**Why it's wrong:** The env var is read once at startup. If the orchestrator rotates the key, the app must restart to pick it up. Worse, if the key is empty at startup (misconfiguration), the value is cached as empty and the endpoint returns 401 forever without any visible failure.

**Do this instead:** Inject `IConfiguration` into the handler and read `configuration["STATUS_API_KEY"]` at request time. `IConfiguration` reads from env vars on every access. The cost is negligible (dictionary lookup).

### Anti-Pattern 3: Mutating the DB Config Object in ClamAVScannerService

**What people do:** Read config from DB, then set `config.Tier1.ClamAV.Host = envHost` before creating the `ClamClient`.

**Why it's wrong:** `FileScanningConfig` is a shared domain model. Mutating a returned object could affect other parts of the call chain if the config were ever cached. It also obscures where the value actually came from in logs.

**Do this instead:** Construct the `ClamClient` with the env var values directly, without touching the config object. Log when an override is active: `_logger.LogInformation("ClamAV connection overridden by env var: {Host}:{Port}", host, port)`.

### Anti-Pattern 4: Placing --bootstrap-owner Before RunDatabaseMigrationsAsync

**What people do:** Add the CLI flag check before `app.Build()` or before migrations run, because "it exits anyway."

**Why it's wrong:** `IAuthService` → `IUserRepository` → `AppDbContext` requires the users table to exist. If migrations haven't run, the bootstrap fails with a cryptic EF Core exception instead of a helpful error.

**Do this instead:** The flag must go after `RunDatabaseMigrationsAsync()` and after `serilogConfig.InitializeAsync()`, in the same location as all other early-exit flags. This is the established invariant in `Program.cs`.

## Scaling Considerations

This project is a single-instance homelab app. These features do not affect scalability. The status endpoint is read-only and stateless; the ClamAV override is per-process configuration; the bootstrap flag runs once and exits.

| Scale | Architecture Adjustments |
|-------|--------------------------|
| 1 instance (target) | No adjustments — all three features are designed for this topology |
| N instances (unsupported) | Status endpoint would report per-instance metrics; bootstrap-owner would need distributed coordination to avoid duplicate owners — not planned |

## Sources

- Direct inspection of `TelegramGroupsAdmin/Program.cs` (CLI flag pattern, startup order)
- Direct inspection of `TelegramGroupsAdmin/Services/AuthService.cs` (`RegisterAsync` → `CreateOwnerAccountAsync` private path)
- Direct inspection of `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs` (`CreateClamClientAsync` → DB config read)
- Direct inspection of `TelegramGroupsAdmin/WebApplicationExtensions.cs` (`MapApiEndpoints`, health check registration)
- Direct inspection of `TelegramGroupsAdmin/Endpoints/AuthEndpoints.cs` (existing endpoint structure pattern)
- `TelegramGroupsAdmin/CLAUDE.md` (stack versions, architectural constraints)
- `.planning/PROJECT.md` (milestone scope, key decisions)

---
*Architecture research for: TelegramGroupsAdmin v1.2 SaaS Hosting Readiness*
*Researched: 2026-03-18*
