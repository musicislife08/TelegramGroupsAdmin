# Phase 09: ClamAV Environment Variable Override - Research

**Researched:** 2026-03-18
**Domain:** .NET environment variable override, ClamAVScannerService internals, one-shot logging
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **Override mechanism:** Intercept in `ClamAVScannerService.CreateClamClientAsync()` — the single point where `ClamClient` is constructed
- **Env var check:** Both `CLAMAV_HOST` and `CLAMAV_PORT` must be present for override to activate; if either is missing, fall back to DB config via `GetConfigAsync()`
- **Override cadence:** Read per-scan (every call to `CreateClamClientAsync`), not cached at startup — mirrors the existing per-scan DB config read pattern
- **Startup behavior:** Fail-open; no startup validation when env vars are set; no change to startup sequence
- **Logging:** INFO log on first scan that uses env var override showing effective host:port; no log spam — log once, not per-scan

### Claude's Discretion

- Exact log message wording
- Whether to use a `bool _hasLoggedOverride` flag or similar for one-time logging
- Test approach for verifying override behavior

### Deferred Ideas (OUT OF SCOPE)

- Settings UI "(overridden by env var)" badge — UX-01, future milestone
- Startup ping validation when env vars set — decided against (fail-open)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| CLAM-01 | `CLAMAV_HOST` and `CLAMAV_PORT` env vars override DB-stored ClamAV host and port at scan time (per-scan, not cached at startup) | `CreateClamClientAsync()` is the single construction point; env vars read via `Environment.GetEnvironmentVariable` each call |
| CLAM-02 | Both env vars are required for override to activate — if either is missing, DB config is used (no partial override) | Simple null-check on both values before branching; existing `GetConfigAsync()` path unchanged for the fallback |
| CLAM-03 | When env var override is active, an INFO log records the effective ClamAV host:port on first use | `volatile bool _hasLoggedOverride` field on the service, set after first INFO log; `ILogger.LogInformation` call site |
| CLAM-04 | `GetHealthAsync()` uses the same override-aware config path as `ScanFileAsync()` | Both methods already call `CreateClamClientAsync()` — change to that method automatically covers both; `GetHealthAsync` also logs host:port from DB config currently, needs to use effective values post-change |
</phase_requirements>

## Summary

Phase 9 is a surgical two-method change to `ClamAVScannerService`. The service already has a single client-construction chokepoint (`CreateClamClientAsync`) shared by both `ScanFileAsync` and `GetHealthAsync`, which means there is exactly one place to add the override logic.

The project reads env vars directly via `Environment.GetEnvironmentVariable` (established pattern in `VideoFrameExtractionService`, `ImageTextExtractionService`, `AppDbContextFactory`, `Program.cs`). The OTEL/Seq pattern in `Program.cs` (check string, branch on whether it is set) is the closest structural analog — check both, only apply when both present, otherwise fall through unchanged.

The one-shot logging requirement means the service needs internal state: a `volatile bool` field (`_hasLoggedOverride`) that starts `false` and is set to `true` after the first INFO log fires. Using `volatile` is appropriate here because `ClamAVScannerService` is likely registered as a scoped or singleton service and concurrent scan requests could race on the flag; `volatile` provides the cheapest acceptable guarantee for a best-effort "log once" semantic.

**Primary recommendation:** Add `volatile bool _hasLoggedOverride` and override logic in `CreateClamClientAsync`; also fix the `GetHealthAsync` log message to reflect effective (post-override) host:port, not the raw DB config values.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| nClam | (existing) | ClamClient construction | Already in use; `new ClamClient(host, port)` is the call site to influence |
| Microsoft.Extensions.Logging | (existing) | Structured logging | `ILogger<ClamAVScannerService>` already injected |
| System (BCL) | net10.0 | `Environment.GetEnvironmentVariable` | Established project pattern, no extra dependency |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| NSubstitute | (existing) | Mock `ISystemConfigRepository` in unit tests | Needed to verify override path skips DB call under specific conditions |
| NUnit | (existing) | Test framework | `TelegramGroupsAdmin.UnitTests` project |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `volatile bool` flag | `Interlocked.CompareExchange` | Interlocked gives stronger atomicity but "log once" is best-effort anyway; `volatile` is simpler and sufficient |
| `volatile bool` flag | `Lazy<bool>` | Over-engineered for a simple flag; `Lazy<T>` is for deferred init, not post-init signalling |
| Direct `Environment.GetEnvironmentVariable` in method | Inject `IConfiguration` | `IConfiguration` would require constructor change; direct env var read matches all other ContentDetection services and is consistent |

**No additional installation needed** — all required libraries are already present in the project.

## Architecture Patterns

### Existing Code Structure (ContentDetection)
```
TelegramGroupsAdmin.ContentDetection/
├── Services/
│   ├── ClamAVScannerService.cs       ← ONLY FILE TO CHANGE
│   ├── VirusTotalScannerService.cs
│   └── ...
└── ...

TelegramGroupsAdmin.UnitTests/
├── ContentDetection/
│   ├── AIContentCheckTests.cs
│   └── ...                           ← NEW: ClamAVScannerServiceTests.cs here
```

### Pattern 1: Per-call env var check (established project pattern)
**What:** Read env var inside the method body on every call, not cached at startup
**When to use:** When the decision point is inside a service method that runs per-operation
**Example:**
```csharp
// Source: TelegramGroupsAdmin.ContentDetection/Services/ImageTextExtractionService.cs (line 45)
_tessDataPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX")
    ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
```

### Pattern 2: Conditional branch on env var (OTEL pattern)
**What:** Check for non-null/non-empty, then choose code path
**When to use:** When env var presence selects one of two fully different behaviors
**Example:**
```csharp
// Source: TelegramGroupsAdmin/Program.cs (line 95)
if (!string.IsNullOrEmpty(seqUrl))
{
    loggerConfig.WriteTo.Seq(seqUrl, apiKey: seqApiKey);
}
```

### Pattern 3: One-shot logging with volatile flag
**What:** Service-level `volatile bool` field; log once on first activation
**When to use:** When a runtime branch activates for the first time and you want exactly one INFO log regardless of concurrency
**Example (to implement):**
```csharp
// Source: to be implemented in ClamAVScannerService
private volatile bool _hasLoggedOverride;

private async Task<ClamClient> CreateClamClientAsync(CancellationToken cancellationToken = default)
{
    var envHost = Environment.GetEnvironmentVariable("CLAMAV_HOST");
    var envPort = Environment.GetEnvironmentVariable("CLAMAV_PORT");

    if (!string.IsNullOrWhiteSpace(envHost) && !string.IsNullOrWhiteSpace(envPort)
        && int.TryParse(envPort, out var parsedPort))
    {
        if (!_hasLoggedOverride)
        {
            _hasLoggedOverride = true;
            _logger.LogInformation(
                "ClamAV env var override active — using {Host}:{Port} (CLAMAV_HOST / CLAMAV_PORT)",
                envHost, parsedPort);
        }
        return new ClamClient(envHost, parsedPort);
    }

    var config = await GetConfigAsync(cancellationToken);
    return new ClamClient(config.Tier1.ClamAV.Host, config.Tier1.ClamAV.Port);
}
```

### Secondary fix: GetHealthAsync host:port logging
**What:** `GetHealthAsync` currently reads `config.Tier1.ClamAV.Host/Port` for its log message AFTER calling `CreateClamClientAsync`. Post-change, the client may be using override values while the log still shows DB values.
**Fix:** Capture effective host:port from the context available, or accept that health log message reflects DB config (it is only a Debug log at the start). The success log at line 329 is INFO level and uses `config.Tier1.ClamAV.Host/Port` — these need to reflect the effective values.
**Approach:** Refactor `CreateClamClientAsync` to return a value tuple `(ClamClient client, string effectiveHost, int effectivePort)` OR extract a separate helper `GetEffectiveConnectionAsync()` that returns host/port, then `CreateClamClientAsync` calls it. The simpler option is a struct or record return; but since all callers currently just use the `ClamClient`, the least-invasive fix is to have a small `GetEffectiveEndpointAsync` private method returning `(string host, int port)` and calling that from both `CreateClamClientAsync` and `GetHealthAsync`.

**Alternative (simpler):** Keep `CreateClamClientAsync` as-is for the ClamClient construction, but also expose the effective endpoint to `GetHealthAsync` via a private helper. Given `GetHealthAsync` calls `CreateClamClientAsync` anyway, calling the new `GetEffectiveEndpointAsync` before `CreateClamClientAsync` adds a second DB read but is simplest to read.

**Recommendation:** Factor out `GetEffectiveEndpointAsync()` returning `(string host, int port)`. Both `CreateClamClientAsync` and `GetHealthAsync` call it. Eliminates duplication of override logic and ensures logging is consistent.

### Anti-Patterns to Avoid
- **Caching env var at startup:** Violates CLAM-01 (per-scan, not cached). Would also prevent live updates in containerised environments.
- **Partial override (only HOST or only PORT):** Explicitly disallowed by CLAM-02; partial override silently changes one dimension while keeping the other, likely breaking the connection.
- **Logging on every scan:** Creates log spam under normal operation; log once only.
- **Mutating `config.Tier1.ClamAV.Host/Port` in memory:** The `FileScanningConfig` object is a shared DTO; mutating it would corrupt the DB-sourced value for other uses.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| One-time logging across async concurrent calls | Complex lock / lazy init | `volatile bool` field | Sufficient for best-effort; simpler, no allocation |
| Port string → int conversion | Custom parser | `int.TryParse` | BCL, handles edge cases, returns bool for validation |
| Env var reading | Custom env abstraction | `Environment.GetEnvironmentVariable` | Matches all existing ContentDetection services |

**Key insight:** This phase has zero new library requirements. All primitives needed (`volatile`, `int.TryParse`, `Environment.GetEnvironmentVariable`, `ILogger.LogInformation`) are BCL or already injected.

## Common Pitfalls

### Pitfall 1: Port parsing failure silently falls through
**What goes wrong:** `CLAMAV_PORT=notanumber` causes `int.TryParse` to return `false`; if the condition short-circuits without checking the parse result, the service may silently use DB config even though `CLAMAV_HOST` was set.
**Why it happens:** Forgetting to validate `parsedPort` from `int.TryParse` in the compound condition.
**How to avoid:** Use `int.TryParse(envPort, out var parsedPort)` as part of the `if` condition. If parse fails, treat as "env vars not fully set" and fall back to DB.
**Warning signs:** Override appears inactive even with both vars set.

### Pitfall 2: GetHealthAsync logs show wrong host:port
**What goes wrong:** `GetHealthAsync` calls `CreateClamClientAsync` (which uses override) but then logs `config.Tier1.ClamAV.Host/Port` from a separate `GetConfigAsync` call, showing DB values even when override is active.
**Why it happens:** `GetHealthAsync` has its own `GetConfigAsync` call at the top (line 309 in current code) used purely for the log message. After the refactor, that data is stale if override is active.
**How to avoid:** Factor out `GetEffectiveEndpointAsync()` and use its return value for all log messages in `GetHealthAsync`.
**Warning signs:** CLAM-04 fails ("health check uses same override-aware config path") if verification includes checking the log output.

### Pitfall 3: CLAMAV_PORT env var is empty string
**What goes wrong:** `Environment.GetEnvironmentVariable("CLAMAV_PORT")` returns `""` (set but empty), and `string.IsNullOrWhiteSpace` catches it, but if only checking `!= null`, it would attempt `int.TryParse("")` which returns `false`.
**Why it happens:** Container orchestrators (docker-compose, Kubernetes) sometimes pass empty string for unset env vars.
**How to avoid:** Use `string.IsNullOrWhiteSpace` (not `!= null`) on both env vars before the parse attempt.

### Pitfall 4: Test environment pollution
**What goes wrong:** A test sets `CLAMAV_HOST` / `CLAMAV_PORT` as process env vars and subsequent tests in the suite pick them up unexpectedly.
**Why it happens:** `Environment.SetEnvironmentVariable` persists for the process lifetime in the same test runner.
**How to avoid:** Tests that exercise the override path must clean up env vars in `[TearDown]`, or set them only for the scope of the test and clear them after.

## Code Examples

Verified patterns from project source:

### Current CreateClamClientAsync (insertion point)
```csharp
// Source: TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs lines 37-43
private async Task<ClamClient> CreateClamClientAsync(CancellationToken cancellationToken = default)
{
    var config = await GetConfigAsync(cancellationToken);
    return new ClamClient(
        config.Tier1.ClamAV.Host,
        config.Tier1.ClamAV.Port);
}
```

### Env var read + IsNullOrWhiteSpace pattern (established)
```csharp
// Source: TelegramGroupsAdmin.ContentDetection/Services/ImageTextExtractionService.cs line 45
_tessDataPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX")
    ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
```

### Conditional env var branch (established)
```csharp
// Source: TelegramGroupsAdmin/Program.cs lines 95-98
if (!string.IsNullOrEmpty(seqUrl))
{
    loggerConfig.WriteTo.Seq(seqUrl, apiKey: seqApiKey);
}
```

### NSubstitute mock for ISystemConfigRepository (test pattern)
```csharp
// Adapted from existing unit test patterns in TelegramGroupsAdmin.UnitTests
var mockConfigRepo = Substitute.For<ISystemConfigRepository>();
mockConfigRepo.GetAsync(chatId: null, Arg.Any<CancellationToken>())
    .Returns(Task.FromResult(new FileScanningConfig
    {
        Tier1 = new Tier1Config
        {
            ClamAV = new ClamAVConfig { Host = "db-host", Port = 3310, Enabled = true }
        }
    }));
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| N/A — no env var override existed | Per-scan env var override in `CreateClamClientAsync` | Phase 9 (now) | Operators can share a single clamd daemon across TGA instances without DB seeding |

**No deprecated APIs involved.** `Environment.GetEnvironmentVariable` is stable BCL. `nClam`'s `ClamClient(string, int)` constructor is unchanged.

## Open Questions

1. **DI lifetime of ClamAVScannerService**
   - What we know: The service is registered in `TelegramGroupsAdmin.ContentDetection` DI setup
   - What's unclear: Whether it is Singleton or Scoped — affects whether `volatile bool _hasLoggedOverride` works as expected (if Scoped, a new instance per request means the flag resets each scan; if Singleton, flag persists across all scans)
   - Recommendation: Verify registration before writing the flag. If Scoped, use a static field (`private static volatile bool _hasLoggedOverride`) to ensure the flag survives across instances. A static field is acceptable for a "log once per process" semantic.

2. **`GetEffectiveEndpointAsync` vs. inline duplication**
   - What we know: The factored-out helper is cleaner and avoids duplicating the override logic; the inline approach is fewer lines but duplicates the condition
   - What's unclear: Whether the planner should treat this as one task or two (CreateClamClientAsync change + GetHealthAsync fix)
   - Recommendation: Treat as a single plan task; both changes are in the same file and method-level.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | NUnit 3 (TelegramGroupsAdmin.UnitTests) |
| Config file | none (implicit via NUnit3TestAdapter) |
| Quick run command | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "FullyQualifiedName~ClamAVScannerService"` |
| Full suite command | `dotnet test TelegramGroupsAdmin.UnitTests --no-build` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CLAM-01 | When both CLAMAV_HOST and CLAMAV_PORT are set, ClamClient is constructed with env var values, not DB values | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "FullyQualifiedName~ClamAVScannerServiceTests"` | ❌ Wave 0 |
| CLAM-02 | When only one of CLAMAV_HOST or CLAMAV_PORT is set, DB config is used | unit | (same filter) | ❌ Wave 0 |
| CLAM-03 | INFO log fires exactly once when override is active across two consecutive scans | unit | (same filter) | ❌ Wave 0 |
| CLAM-04 | GetHealthAsync connects to override host:port when both vars set | unit | (same filter) | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "FullyQualifiedName~ClamAVScannerServiceTests"`
- **Per wave merge:** `dotnet test TelegramGroupsAdmin.UnitTests --no-build`
- **Phase gate:** Full unit suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `TelegramGroupsAdmin.UnitTests/ContentDetection/ClamAVScannerServiceTests.cs` — covers CLAM-01, CLAM-02, CLAM-03, CLAM-04

*(Existing test infrastructure covers all other phase requirements — only the new test file is missing)*

## Sources

### Primary (HIGH confidence)
- `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs` — full service reviewed; insertion point confirmed
- `TelegramGroupsAdmin.Configuration/Models/ClamAVConfig.cs` — Host/Port field names confirmed
- `TelegramGroupsAdmin.Configuration/Repositories/ISystemConfigRepository.cs` — GetAsync signature confirmed
- `TelegramGroupsAdmin/Program.cs` lines 74-98 — env var reading + conditional branch pattern

### Secondary (MEDIUM confidence)
- `TelegramGroupsAdmin.ContentDetection/Services/ImageTextExtractionService.cs` lines 45, 209 — env var read pattern with IsNullOrWhiteSpace fallback
- `TelegramGroupsAdmin.ContentDetection/Services/VideoFrameExtractionService.cs` lines 642, 663 — same env var pattern
- `TelegramGroupsAdmin.UnitTests/Services/BanCelebrationServiceTests.cs` — NSubstitute + NUnit test structure pattern

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries already in use, zero new dependencies
- Architecture: HIGH — insertion point (`CreateClamClientAsync`) verified directly in source; all patterns established in codebase
- Pitfalls: HIGH — derived from direct code inspection and established .NET patterns

**Research date:** 2026-03-18
**Valid until:** 2026-04-18 (stable domain; only changes if nClam or .NET BCL env var API changes, which is extremely unlikely)
