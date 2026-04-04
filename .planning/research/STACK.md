# Stack Research

**Domain:** SaaS hosting readiness additions to existing .NET 10 Blazor Server app
**Researched:** 2026-03-18
**Confidence:** HIGH

## Context

This is a brownfield milestone. The existing stack (.NET 10, Blazor Server, EF Core 10, PostgreSQL 18, Quartz.NET, nClam, MudBlazor 9) is validated and unchanged. This document covers only the three new capabilities introduced in v1.2:

1. `--bootstrap-owner` CLI flag for headless owner account creation
2. `CLAMAV_HOST` / `CLAMAV_PORT` env var override of DB-stored ClamAV config
3. `GET /healthz/status` JSON endpoint with runtime metrics, gated behind `STATUS_API_KEY`

---

## Feature 1: --bootstrap-owner CLI Flag

### What Already Exists

`AuthService.CreateOwnerAccountAsync()` (line 266, `TelegramGroupsAdmin/Services/AuthService.cs`) already implements the full owner-creation logic: generates a user ID, hashes the password, sets `PermissionLevel.Owner`, writes the audit log, conditionally skips email verification. The `RegisterAsync` path calls this when `IsFirstRunAsync()` returns true.

The CLI flag pattern already exists for `--migrate-only`, `--backup`, and `--restore` in `Program.cs` (lines 186–273). All three follow the same shape: `if (args.Contains("--flag"))` block after `app.Build()`, resolve required services from `app.Services.CreateScope()`, execute, then `Environment.Exit(0)`.

### What Is New

A `--bootstrap-owner` block in `Program.cs` that:
- Reads `--email` and `--password` from the `args` array (same positional pattern as `--backup`'s path argument)
- Calls `IAuthService.RegisterAsync(email, password, inviteToken: null)` — this already routes to `CreateOwnerAccountAsync()` when no users exist
- Exits 0 on success, exits 1 with a logged error if users already exist (non-idempotent by design — bootstrapping a pre-populated instance is an error)

### No New Packages Required

All dependencies (`IAuthService`, `IPasswordHasher`, `IUserRepository`, `IAuditService`) are already registered. The `--bootstrap-owner` block must run **after** `await app.RunDatabaseMigrationsAsync()` (so the schema exists) and **before** ML training (no classifier needed for user creation). Placement: between the existing `--migrate-only` exit and the `--backup` check.

**Confidence:** HIGH — pattern is fully established in the codebase.

---

## Feature 2: CLAMAV_HOST / CLAMAV_PORT Env Var Override

### What Already Exists

`ClamAVConfig` (`TelegramGroupsAdmin.Configuration/Models/ClamAVConfig.cs`) stores `Host` (default: `"localhost"`) and `Port` (default: `3310`). `ClamAVScannerService.CreateClamClientAsync()` (line 37) calls `_configRepository.GetAsync()` to read these values from the database JSONB column on every scan attempt.

### What Is New

The override must intercept the DB-read result and substitute env var values when present. The cleanest approach is a thin wrapper or modification in `ClamAVScannerService.CreateClamClientAsync()`:

```csharp
// In ClamAVScannerService constructor — inject IConfiguration
// In CreateClamClientAsync():
var host = Environment.GetEnvironmentVariable("CLAMAV_HOST") ?? config.Tier1.ClamAV.Host;
var port = int.TryParse(Environment.GetEnvironmentVariable("CLAMAV_PORT"), out var p)
    ? p
    : config.Tier1.ClamAV.Port;
return new ClamClient(host, port);
```

Using `Environment.GetEnvironmentVariable` directly (not `IConfiguration`) is correct here: the value is infrastructure-level and should not be logged, cached, or overridden by appsettings. `IConfiguration` is acceptable as an alternative since the project uses `builder.Configuration` for env vars — either approach works; direct env var read is simpler and avoids DI changes.

**No `IConfiguration` injection is needed** in `ClamAVScannerService` — it does not currently receive it, and adding it is unnecessary complexity. `Environment.GetEnvironmentVariable` is the right tool.

### No New Packages Required

`Environment.GetEnvironmentVariable` is BCL. `ClamAVScannerService` already receives `ISystemConfigRepository` and `ILogger<ClamAVScannerService>` — no new constructor parameters needed.

**Confidence:** HIGH — pure BCL, no external dependencies.

---

## Feature 3: GET /healthz/status JSON Endpoint

### Security: API Key Gate

The endpoint must return 401 when `STATUS_API_KEY` is absent or the request header does not match. Use a minimal API endpoint with inline header validation — do not add JWT bearer auth or any new auth scheme. The existing auth system is cookie-based for the UI; mixing bearer tokens into that scheme adds complexity.

**Pattern:**

```csharp
app.MapGet("/healthz/status", (HttpContext ctx, IConfiguration cfg) =>
{
    var expectedKey = Environment.GetEnvironmentVariable("STATUS_API_KEY");
    if (string.IsNullOrEmpty(expectedKey))
        return Results.StatusCode(503); // endpoint disabled when key not configured
    if (!ctx.Request.Headers.TryGetValue("X-Status-Api-Key", out var key) || key != expectedKey)
        return Results.Unauthorized();
    // ... collect metrics and return
}).AllowAnonymous();
```

Returning 503 when `STATUS_API_KEY` is not set (rather than 401 or 200) signals to the hosting provider that the feature is unconfigured, not that the key is wrong.

### Runtime Metrics: BCL APIs (No New Packages)

All needed runtime metrics are available via BCL APIs in .NET 9+ (confirmed in .NET 10):

| Metric | BCL API | Notes |
|--------|---------|-------|
| Working set (bytes) | `Environment.WorkingSet` | Physical memory mapped to process |
| GC heap size (bytes) | `GC.GetGCMemoryInfo().HeapSizeBytes` | Managed heap including fragmentation |
| GC committed (bytes) | `GC.GetGCMemoryInfo().TotalCommittedBytes` | Committed virtual memory |
| GC total allocated (bytes) | `GC.GetTotalAllocatedBytes()` | Cumulative since process start |
| GC gen0 collections | `GC.CollectionCount(0)` | Since process start |
| GC gen1 collections | `GC.CollectionCount(1)` | Since process start |
| GC gen2 collections | `GC.CollectionCount(2)` | Since process start |
| Thread pool threads | `ThreadPool.ThreadCount` | Current worker thread count |
| Thread pool queue depth | `ThreadPool.PendingWorkItemCount` | Work items waiting |
| Process uptime | `Environment.TickCount64` or `Stopwatch` from startup | Milliseconds since process start |

### Blazor Circuit Count: IMeterFactory + MeterListener

Active circuit count is **not** available via a simple BCL property. It is maintained by the ASP.NET Core framework as an OTel UpDownCounter (`aspnetcore.components.circuit.active`). To read it without enabling the full OTEL pipeline, subscribe to it via `MeterListener` (BCL, no new packages):

```csharp
// Register as singleton in DI:
public class CircuitMetricsCollector : IDisposable
{
    private readonly MeterListener _listener;
    private int _activeCircuits;

    public int ActiveCircuits => _activeCircuits;

    public CircuitMetricsCollector()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Microsoft.AspNetCore.Components.Server.Circuits"
                && instrument.Name == "aspnetcore.components.circuit.active")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            Interlocked.Add(ref _activeCircuits, measurement);
        });
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();
}
```

`MeterListener` is in `System.Diagnostics.Metrics` (BCL, .NET 6+). No new NuGet packages required. Register as `services.AddSingleton<CircuitMetricsCollector>()` and inject into the status endpoint handler.

**Alternative considered:** Reading `aspnetcore.components.circuit.active` via OTEL's `IMeterFactory` or Prometheus scraping — both require either OTEL to be enabled or an additional HTTP round-trip. `MeterListener` is the correct direct-read approach for a single value.

### No New Packages Required

`System.Diagnostics.Metrics.MeterListener` is BCL (.NET 6+, available in .NET 10). All other runtime metrics (`Environment.WorkingSet`, `GC.*`, `ThreadPool.*`) are BCL. The endpoint itself is a minimal API (pattern already in use in `WebApplicationExtensions.MapApiEndpoints()`).

**Confidence:** HIGH — all APIs are BCL, confirmed in official .NET docs. MeterListener pattern is the documented approach for consuming metrics outside of OTEL.

---

## Recommended Stack (New Additions Only)

### Core Technologies

No new core technologies. All three features are implemented using existing framework capabilities.

### Supporting Libraries

No new NuGet packages required for any of the three features.

| What | Where | Why No New Package |
|------|-------|-------------------|
| CLI bootstrap | `Program.cs` | Reuses `IAuthService.RegisterAsync()` — already registered |
| ClamAV env override | `ClamAVScannerService.cs` | `Environment.GetEnvironmentVariable()` — BCL |
| Status endpoint auth | `WebApplicationExtensions.cs` | Inline header check — avoids auth scheme pollution |
| Runtime memory metrics | Status endpoint handler | `Environment.WorkingSet`, `GC.*`, `ThreadPool.*` — BCL |
| Circuit count | `CircuitMetricsCollector` singleton | `System.Diagnostics.Metrics.MeterListener` — BCL (.NET 6+) |

### What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Would pollute the existing cookie-auth scheme; adds JWT validation complexity for a single internal endpoint | Inline `X-Status-Api-Key` header check |
| OpenTelemetry for reading circuit count | OTEL is conditionally enabled; reading its data would require the pipeline to be active | `MeterListener` (BCL), always available |
| New IOptions class for `STATUS_API_KEY` | Single env var, read once per request, not injected into multiple services | `Environment.GetEnvironmentVariable()` directly in endpoint |
| `[Authorize]` attribute with a custom scheme | Adds new auth scheme registration, interacts with Blazor auth middleware | `AllowAnonymous()` + manual key check in handler body |
| `appsettings.json` for any of these settings | Project policy prohibits appsettings files | Environment variables only |

### Stack Patterns

**For the CLI flag (`--bootstrap-owner`):**
- Placement in `Program.cs`: after `RunDatabaseMigrationsAsync()`, before ML training
- Use `args.Contains("--bootstrap-owner")` + `args.SkipWhile(...).Skip(1).FirstOrDefault()` for `--email` and `--password` (same pattern as `--backup`)
- Exit 1 if users already exist (idempotency is not a requirement; the SaaS orchestrator calls this exactly once)
- Bypass email verification (same as `CreateOwnerAccountAsync` when email not configured)

**For ClamAV env override:**
- Override happens at `CreateClamClientAsync()` call time, not at startup — this ensures DB config remains authoritative for UI display while env var silently overrides the actual connection
- Log the effective host/port at Debug level when override is active (helps diagnosis)
- `CLAMAV_PORT` parse failure (non-numeric) should fall back to DB value with a warning log, not throw

**For the status endpoint:**
- Mount at `/healthz/status` — consistent with existing `/healthz/live` and `/healthz/ready`
- Return `Content-Type: application/json`
- Shape: flat JSON object (not nested) for easy parsing by hosting provider scripts
- Include `"status": "ok"` as first field for quick grep-ability
- `CircuitMetricsCollector` singleton must be registered before `app.Build()` so it starts listening from process start

## Version Compatibility

All additions use existing package versions. No version changes needed in `Directory.Packages.props`.

| Capability | Minimum .NET Version | TGA's Version | Compatible |
|-----------|---------------------|--------------|-----------|
| `MeterListener` | .NET 6 | .NET 10 | Yes |
| `GC.GetGCMemoryInfo()` | .NET 3 | .NET 10 | Yes |
| `Environment.WorkingSet` | .NET 1 | .NET 10 | Yes |
| `ThreadPool.ThreadCount` | .NET 3 | .NET 10 | Yes |
| `aspnetcore.components.circuit.active` meter | ASP.NET Core 8 | ASP.NET Core 10 | Yes |

## Sources

- [.NET runtime built-in metrics (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-runtime) — confirmed BCL APIs for GC, thread pool, working set; all available .NET 9+ (HIGH confidence)
- [ASP.NET Core built-in metrics (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/built-in?view=aspnetcore-10.0) — confirmed `aspnetcore.components.circuit.active` meter name and `Microsoft.AspNetCore.Components.Server.Circuits` meter name (HIGH confidence)
- Codebase inspection: `Program.cs`, `AuthService.cs`, `ClamAVScannerService.cs`, `WebApplicationExtensions.cs`, `ConfigurationExtensions.cs` — confirmed existing patterns (HIGH confidence)

---
*Stack research for: TelegramGroupsAdmin v1.2 SaaS hosting readiness*
*Researched: 2026-03-18*
