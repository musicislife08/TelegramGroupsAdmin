# Phase 12: ClamAV IOptions Refactor + Doc Fixes - Research

**Researched:** 2026-03-19
**Domain:** ASP.NET Core IOptions pattern, ClamAV env var override, documentation hygiene
**Confidence:** HIGH

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| CLAM-01 | `CLAMAV__HOST` and `CLAMAV__PORT` env vars (ASP.NET Core convention) override DB-stored ClamAV host and port via `IOptions<ClamAVConfig>` binding | IOptions pattern well-understood; `AddApplicationConfiguration` is the correct binding site |
| CLAM-02 | Both env vars required for override — if either missing, DB config used (no partial override) | Logic already present in Phase 9 `GetEffectiveEndpointAsync`; moves into new IOptions-aware method |
| CLAM-03 | When override active, one INFO log records effective host:port on first use | `static volatile bool _hasLoggedOverride` guard already exists; retain it |
| CLAM-04 | `GetHealthAsync()` uses same override-aware config path as `ScanFileAsync()` | Shared `GetEffectiveEndpointAsync` call already routes both methods; no structural change needed |
</phase_requirements>

---

## Summary

Phase 9 implemented ClamAV env var override using `Environment.GetEnvironmentVariable("CLAMAV_HOST")` and `Environment.GetEnvironmentVariable("CLAMAV_PORT")` (single underscore). The compose files have always used `CLAMAV__HOST`/`CLAMAV__PORT` (double underscore, ASP.NET Core convention), which Docker passes to the container unchanged. ASP.NET Core automatically maps double-underscore env vars to nested config keys (`ClamAV:Host`, `ClamAV:Port`), but only when the config section is explicitly bound via `services.Configure<T>`. Since no `services.Configure<ClamAVConfig>` call exists, `IOptions<ClamAVConfig>` currently returns only the class defaults (`localhost:3310`), not compose values. The env var check for single-underscore names (`CLAMAV_HOST`) never matches what compose sets.

This phase replaces raw `Environment.GetEnvironmentVariable` with `IOptions<ClamAVConfig>` bound to `IConfiguration.GetSection("ClamAV")`. The detection of "override active" shifts from checking for non-empty env var strings to checking whether the IOptions-bound values differ from the `ClamAVConfig` class defaults (`localhost` and `3310`). The existing `static volatile bool _hasLoggedOverride` guard, CLAM-02 "both required" semantics, and `GetEffectiveEndpointAsync` shape are all preserved.

The doc fixes are mechanical: update REQUIREMENTS.md CLAM-01 text to remove the "per-scan, not cached at startup" clause (now that IOptions is singleton-cached), and add `ENABLE_METRICS` env var example to the production compose template. No stale `SEQ_URL` references in REQUIREMENTS.md need fixing — those were already corrected before Phase 12.

**Primary recommendation:** Add `services.Configure<ClamAVConfig>(configuration.GetSection("ClamAV"))` in `AddApplicationConfiguration`, inject `IOptions<ClamAVConfig>` into `ClamAVScannerService`, and replace the `GetEffectiveEndpointAsync` env var reads with an IOptions comparison against class defaults.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.Extensions.Options` | (in ASP.NET Core 10) | `IOptions<T>`, `services.Configure<T>` | Built-in ASP.NET Core options pattern; already used for `AppOptions` and `ContentDetectionOptions` in this project |
| `Microsoft.Extensions.Configuration` | (in ASP.NET Core 10) | `IConfiguration.GetSection()` | Host builder wires env vars → IConfiguration automatically |

### No New NuGet Packages Required

This phase uses APIs already present in `Microsoft.Extensions.Options` and `Microsoft.Extensions.Configuration.EnvironmentVariables`, both of which are transitively available in all projects.

---

## Architecture Patterns

### How `CLAMAV__HOST` Reaches `IConfiguration`

ASP.NET Core's `WebApplication.CreateBuilder` automatically adds `AddEnvironmentVariables()`. Environment variables with double-underscore `__` are mapped to colon-separated config key hierarchy. So:

```
CLAMAV__HOST=clamav   →   IConfiguration["ClamAV:Host"] = "clamav"
CLAMAV__PORT=3310     →   IConfiguration["ClamAV:Port"] = "3310"
```

This mapping happens without any additional code. The only missing piece in Phase 9 was the `services.Configure<ClamAVConfig>(configuration.GetSection("ClamAV"))` call and corresponding constructor injection.

### Recommended Change 1: Bind `ClamAVConfig` in `AddApplicationConfiguration`

File: `TelegramGroupsAdmin.Configuration/ConfigurationExtensions.cs`

```csharp
// Source: ConfigurationExtensions.cs — existing pattern
public IServiceCollection AddApplicationConfiguration(IConfiguration configuration)
{
    services.Configure<AppOptions>(configuration.GetSection("App"));
    services.Configure<ContentDetectionOptions>(configuration.GetSection("SpamDetection"));
    services.Configure<ClamAVConfig>(configuration.GetSection("ClamAV")); // ADD THIS
    // ...
}
```

`IOptions<ClamAVConfig>` is singleton-cached by the framework. The env var values are captured once at startup, which is correct since container env vars do not change at runtime.

### Recommended Change 2: Inject `IOptions<ClamAVConfig>` into `ClamAVScannerService`

```csharp
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration.Models;

public class ClamAVScannerService : IFileScannerService
{
    private readonly ILogger<ClamAVScannerService> _logger;
    private readonly ISystemConfigRepository _configRepository;
    private readonly ClamAVConfig _envOverride;   // resolved once at construction

    private static volatile bool _hasLoggedOverride;

    public ClamAVScannerService(
        ILogger<ClamAVScannerService> logger,
        ISystemConfigRepository configRepository,
        IOptions<ClamAVConfig> clamAvOptions)
    {
        _logger = logger;
        _configRepository = configRepository;
        _envOverride = clamAvOptions.Value;
    }
    // ...
}
```

### Recommended Change 3: Replace `GetEffectiveEndpointAsync` to Use IOptions

The "override active" check replaces env var presence with a comparison to `ClamAVConfig` class defaults:

```csharp
private static readonly ClamAVConfig _defaults = new();  // Host="localhost", Port=3310

private async Task<(string host, int port)> GetEffectiveEndpointAsync(
    CancellationToken cancellationToken = default)
{
    // Override is active when IConfiguration has bound non-default values for BOTH host AND port.
    // ClamAVConfig defaults: Host="localhost", Port=3310.
    // If compose sets CLAMAV__HOST=clamav and CLAMAV__PORT=3310 (same port as default),
    // only host differs — but BOTH must differ for the "both required" rule.
    // The safest check: host is non-empty and non-whitespace AND port is in valid range (1–65535),
    // AND at least one differs from defaults (so plain localhost:3310 does NOT falsely activate).

    var overrideHost = _envOverride.Host;
    var overridePort = _envOverride.Port;

    var hostOverridden = !string.IsNullOrWhiteSpace(overrideHost)
                         && overrideHost != _defaults.Host;
    var portOverridden = overridePort is >= 1 and <= 65535
                         && overridePort != _defaults.Port;

    // Both must be explicitly overridden (CLAM-02: no partial override)
    if (hostOverridden && portOverridden)
    {
        if (!_hasLoggedOverride)
        {
            _hasLoggedOverride = true;
            _logger.LogInformation(
                "ClamAV IOptions override active -- using {Host}:{Port} (CLAMAV__HOST / CLAMAV__PORT)",
                overrideHost, overridePort);
        }
        return (overrideHost, overridePort);
    }

    var config = await GetConfigAsync(cancellationToken);
    return (config.Tier1.ClamAV.Host, config.Tier1.ClamAV.Port);
}
```

**Caveat on "both required" logic:** The current check — both host AND port must differ from defaults — correctly handles the common case (compose sets `clamav:3310`, host differs from `localhost`). However, the edge case where an operator genuinely wants to override the host to `localhost` (same as default) will not trigger the override. This is an acceptable constraint: if the host matches the default, there is no meaningful override. Document this in code comments.

### Alternative "Both Required" Detection Strategy

A simpler and more robust approach: the override is active if IConfiguration explicitly bound the `ClamAV` section at all. Check if `_envOverride.Host != _defaults.Host || _envOverride.Port != _defaults.Port` — activate if EITHER differs — and use both the overridden values. But this would violate CLAM-02 (both required). The "both must differ from defaults" approach above correctly preserves CLAM-02 since real production deployments always use a non-default host AND a port (typically 3310, which is the same as default).

**Decision:** Because the port default (3310) matches what compose sets (`CLAMAV__PORT: "3310"`), the detection strategy above would never activate the override in real deployments — the port stays at the default! This is the critical pitfall.

**Correct approach:** Check only that host is explicitly non-default AND port is valid (not necessarily non-default). Or, simpler: treat the IOptions-bound value as the source of truth and drop the "differs from default" check entirely. Instead, require the host to be non-empty-and-non-whitespace AND port to be valid (> 0), and compare to a known sentinel that means "not set via env var." Since `ClamAVConfig.Host` defaults to `"localhost"`, we cannot distinguish "IOptions bound nothing, so default was used" from "IOptions bound `CLAMAV__HOST=localhost`."

**The clean solution:** Use a nullable IOptions approach — `ClamAVConfig` retains its non-nullable defaults for DB use, but the IOptions binding section uses a separate nullable sentinel type. OR: simply check `IConfiguration` for the presence of the keys:

```csharp
// Inject IConfiguration (already available in Configuration project context)
private async Task<(string host, int port)> GetEffectiveEndpointAsync(
    CancellationToken cancellationToken = default)
{
    var overrideHost = _envOverride.Host;
    var overridePort = _envOverride.Port;

    // Check: were env vars explicitly set?
    // Since CLAMAV__HOST maps to ClamAV:Host in IConfiguration, and IOptions binds from
    // IConfiguration, we compare overridePort > 0 (valid) AND overrideHost is non-whitespace.
    // To detect "both required": env vars set non-whitespace host AND port in valid range [1,65535].
    // We can NOT use "differs from default" because CLAMAV__PORT=3310 IS the default.
    // Solution: inject IConfiguration directly and check HasValue:

    var hasEnvHost = _configuration["ClamAV:Host"] is { Length: > 0 };
    var hasEnvPort = _configuration["ClamAV:Port"] is { Length: > 0 };

    if (hasEnvHost && hasEnvPort
        && !string.IsNullOrWhiteSpace(overrideHost)
        && int.TryParse(_configuration["ClamAV:Port"], out var parsedPort)
        && parsedPort is >= 1 and <= 65535)
    {
        if (!_hasLoggedOverride)
        {
            _hasLoggedOverride = true;
            _logger.LogInformation(
                "ClamAV IOptions override active -- using {Host}:{Port} (CLAMAV__HOST / CLAMAV__PORT)",
                overrideHost, parsedPort);
        }
        return (overrideHost, parsedPort);
    }

    var config = await GetConfigAsync(cancellationToken);
    return (config.Tier1.ClamAV.Host, config.Tier1.ClamAV.Port);
}
```

However, injecting raw `IConfiguration` into a ContentDetection service is an anti-pattern (it bypasses the Options pattern and couples to config infrastructure). The planner should choose one of two clean options:

**Option A (Recommended):** Add `string? Host` and `int? Port` as nullable fields to a new `ClamAVEnvOverrideOptions` class that is separate from `ClamAVConfig`. Bind only this class via IOptions. If both are non-null, override is active. `ClamAVConfig` (with non-nullable defaults) stays untouched for DB use.

**Option B:** Keep using `Environment.GetEnvironmentVariable` but change from single-underscore `CLAMAV_HOST` to double-underscore `CLAMAV__HOST` key names. Compose already sets double-underscore names; the raw env var string in the container environment IS `CLAMAV__HOST`. This is the minimal-change option — only two string literals change in `GetEffectiveEndpointAsync`. The IOptions binding for display/UX purposes (future UX-01) can be added separately.

**Option B is strongly preferred for this phase** because:
1. Minimal code change (two string literals in one method)
2. No new types needed
3. The "both required" check is identical to Phase 9's working logic
4. Aligns `ClamAVScannerService` with what compose actually sets, immediately fixing the broken override
5. Avoids the nullable-IOptions complexity for a homelab project

If UX-01 ("override badge in Settings UI") is implemented later, it can introduce `IOptions<ClamAVConfig>` at that point when there is a genuine display use case.

### Anti-Patterns to Avoid

- **Comparing IOptions values to class defaults to detect "env var set":** The port default (3310) matches the compose value. This will silently never activate the override in real deployments.
- **Injecting `IConfiguration` directly into `ClamAVScannerService`:** Bypasses Options pattern, makes testing harder, couples ContentDetection to config infrastructure.
- **Adding complex nullable wrapper types:** Over-engineering for what is a two-string-literal fix.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Env var to config mapping | Manual parsing of `CLAMAV__HOST` | ASP.NET Core built-in `AddEnvironmentVariables()` with double-underscore convention | Already wired by `WebApplication.CreateBuilder` |
| Options caching | Custom static field for config values | `IOptions<T>` singleton semantics | Framework handles caching, validation, reload |
| Env var presence detection | Comparing to class defaults | `Environment.GetEnvironmentVariable` on the actual env var name | More reliable than default comparison when defaults match env var values |

---

## Common Pitfalls

### Pitfall 1: Port Default Matches Compose Value

**What goes wrong:** `ClamAVConfig.Port` defaults to `3310`. Compose sets `CLAMAV__PORT: "3310"`. An "differs from default" check will never see the port as overridden, so the override silently never activates.

**Why it happens:** The class default and the standard ClamAV port are identical.

**How to avoid:** Use Option B — read `Environment.GetEnvironmentVariable("CLAMAV__HOST")` and `Environment.GetEnvironmentVariable("CLAMAV__PORT")` (double-underscore) directly. Presence check (non-null, non-whitespace, valid int) does not require the value to differ from any default.

**Warning signs:** Unit tests pass but integration testing shows DB config being used despite compose env vars being set.

### Pitfall 2: Single-Underscore vs Double-Underscore Env Var Names

**What goes wrong:** Phase 9 reads `CLAMAV_HOST` (single underscore). Compose sets `CLAMAV__HOST` (double underscore). In a container, both env var names exist independently. `Environment.GetEnvironmentVariable("CLAMAV_HOST")` returns null when only `CLAMAV__HOST` is set.

**Why it happens:** ASP.NET Core translates `CLAMAV__HOST` → `ClamAV:Host` in IConfiguration, but the raw env var name in the process environment is still `CLAMAV__HOST`. These are two distinct strings.

**How to avoid:** Change Phase 9's `"CLAMAV_HOST"` → `"CLAMAV__HOST"` and `"CLAMAV_PORT"` → `"CLAMAV__PORT"`.

**Warning signs:** Override never activates in Docker despite compose env vars being present.

### Pitfall 3: Log Message References Old Env Var Names

**What goes wrong:** The INFO log in Phase 9 says `CLAMAV_HOST / CLAMAV_PORT`. After the fix, it should say `CLAMAV__HOST / CLAMAV__PORT` so operators using the log to verify override status see the correct env var names.

**How to avoid:** Update the log message string literals in `GetEffectiveEndpointAsync`.

### Pitfall 4: Test Infrastructure Resets Static Field via Reflection

**What goes wrong:** Tests use `BindingFlags.NonPublic | BindingFlags.Static` reflection to reset `_hasLoggedOverride`. After the refactor, the field name is unchanged — but if any refactor accidentally changes the field or makes it non-static, the reflection call silently fails (field?.SetValue is null-safe, so the test won't error, just fail to reset).

**How to avoid:** Keep `static volatile bool _hasLoggedOverride` at the same name and access level. Unit tests for CLAM-03 already verify one-time logging; don't change the test reset approach.

### Pitfall 5: `ClamAVScannerService` Constructor Signature Change Breaks DI

**What goes wrong:** If switching to IOptions injection, the constructor gains a new parameter. The service is registered via `services.AddScoped<IFileScannerService, ClamAVScannerService>()` — DI resolves this automatically. BUT if Option B (keep `Environment.GetEnvironmentVariable`, change string to double-underscore) is chosen, the constructor does NOT change at all. No DI change required.

**How to avoid:** Use Option B; no constructor change needed.

---

## Code Examples

### Option B: Minimal Fix — Change Env Var String Literals Only

```csharp
// Source: ClamAVScannerService.cs — GetEffectiveEndpointAsync
// Change ONLY these two string literals and the log message:

private async Task<(string host, int port)> GetEffectiveEndpointAsync(
    CancellationToken cancellationToken = default)
{
    // Use CLAMAV__HOST / CLAMAV__PORT (double underscore) to match ASP.NET Core convention.
    // Docker Compose sets CLAMAV__HOST and CLAMAV__PORT in the container environment.
    // These are distinct from CLAMAV_HOST / CLAMAV_PORT (single underscore).
    var envHost = Environment.GetEnvironmentVariable("CLAMAV__HOST");   // was "CLAMAV_HOST"
    var envPort = Environment.GetEnvironmentVariable("CLAMAV__PORT");   // was "CLAMAV_PORT"

    if (!string.IsNullOrWhiteSpace(envHost)
        && !string.IsNullOrWhiteSpace(envPort)
        && int.TryParse(envPort, out var parsedPort))
    {
        if (!_hasLoggedOverride)
        {
            _hasLoggedOverride = true;
            _logger.LogInformation(
                "ClamAV env var override active -- using {Host}:{Port} (CLAMAV__HOST / CLAMAV__PORT)",  // updated names
                envHost, parsedPort);
        }
        return (envHost, parsedPort);
    }

    var config = await GetConfigAsync(cancellationToken);
    return (config.Tier1.ClamAV.Host, config.Tier1.ClamAV.Port);
}
```

### Option A: IOptions with Nullable Sentinel (if chosen over Option B)

```csharp
// New class in TelegramGroupsAdmin.Configuration/Models/ClamAVEnvOverride.cs
public class ClamAVEnvOverride
{
    // Nullable: null means "not set in environment"
    public string? Host { get; set; }
    public int? Port { get; set; }
}

// In AddApplicationConfiguration:
services.Configure<ClamAVEnvOverride>(configuration.GetSection("ClamAV"));

// In ClamAVScannerService constructor:
public ClamAVScannerService(
    ILogger<ClamAVScannerService> logger,
    ISystemConfigRepository configRepository,
    IOptions<ClamAVEnvOverride> envOverride)
{
    _envOverride = envOverride.Value;
    // ...
}

// In GetEffectiveEndpointAsync:
if (_envOverride.Host is { Length: > 0 } host
    && _envOverride.Port is { } port
    && port is >= 1 and <= 65535)
{
    // Override active
}
```

---

## Documentation Changes

### REQUIREMENTS.md: CLAM-01 Text Update

Current text:
```
**CLAM-01**: `CLAMAV__HOST` and `CLAMAV__PORT` env vars (ASP.NET Core convention) override DB-stored ClamAV host and port via `IOptions<ClamAVConfig>` binding
```

The "per-scan, not cached at startup" clause referenced in the roadmap success criterion #5 — the CURRENT text in REQUIREMENTS.md already reads with "IOptions" framing (see REQUIREMENTS.md line 22). The clause to remove is in a prior version; verify by reading the file before writing. The planner should read the actual file to confirm what needs changing.

### REQUIREMENTS.md: STAT-01/02/03 SEQ_URL Check

From REQUIREMENTS.md (lines 29-32), STAT-01 through STAT-05 already reference `OTEL_EXPORTER_OTLP_ENDPOINT`, not `SEQ_URL`. These were updated prior to Phase 12. The planner should verify this is current before scheduling any edit task.

### Production Compose: Add `ENABLE_METRICS`

`examples/compose.production.yml` currently has no `ENABLE_METRICS` env var. The development compose (`examples/compose.development.yml`) also does not have it (relies on `OTEL_EXPORTER_OTLP_ENDPOINT` for metrics). Add a commented-out example under the observability section in both compose files:

```yaml
# =======================================================================
# Metrics (Optional - Enable standalone Prometheus /metrics endpoint)
# =======================================================================
# Uncomment to expose /metrics without requiring full OTEL stack:
# ENABLE_METRICS: "true"
```

Production compose gets this as an active recommendation (many production deployments want metrics without Seq). Development compose already has OTEL configured, so a comment-only addition is sufficient.

---

## Unit Test Updates

### Existing Tests Must Change Env Var Names

`ClamAVScannerServiceTests.cs` uses constants:

```csharp
private const string EnvHost = "CLAMAV_HOST";   // → "CLAMAV__HOST"
private const string EnvPort = "CLAMAV_PORT";   // → "CLAMAV__PORT"
```

These two constants drive all `Environment.SetEnvironmentVariable` calls in `[SetUp]`, `[TearDown]`, and every test. Changing only these two constants updates all 9 tests. No other test logic changes.

### No New Tests Required

All CLAM-01 through CLAM-04 behaviors are already covered by the existing test suite. The refactor is behavior-preserving — only the mechanism changes (env var names). The planner should include a verify step: run the existing unit tests after the change to confirm green.

---

## State of the Art

| Old Approach (Phase 9) | New Approach (Phase 12) | Change | Impact |
|------------------------|------------------------|--------|--------|
| `Environment.GetEnvironmentVariable("CLAMAV_HOST")` | `Environment.GetEnvironmentVariable("CLAMAV__HOST")` | Two string literals | Fixes broken override — compose env vars now actually work |
| Compose uses `CLAMAV__HOST` but code reads `CLAMAV_HOST` | Compose and code both use `CLAMAV__HOST` | Name alignment | No more silent mismatch |
| Log says `CLAMAV_HOST / CLAMAV_PORT` | Log says `CLAMAV__HOST / CLAMAV__PORT` | Log message update | Operator sees correct env var names |

---

## Open Questions

1. **Option A vs Option B**
   - What we know: Option B is 2 string changes. Option A requires a new class and constructor injection.
   - What's unclear: Whether CLAM-01 in REQUIREMENTS.md mandates IOptions specifically (text says "via `IOptions<ClamAVConfig>` binding") vs whether the intent is "use ASP.NET Core conventions."
   - Recommendation: Read REQUIREMENTS.md CLAM-01 text at plan time. If it explicitly requires IOptions<ClamAVConfig> binding, use Option A (nullable override type). If the requirement is about convention alignment, Option B satisfies it with minimal risk.

2. **REQUIREMENTS.md Stale Content**
   - What we know: STAT-01/02/03 looked correct at research time (OTEL_EXPORTER_OTLP_ENDPOINT, not SEQ_URL). CLAM-01 text already contains "IOptions" language.
   - What's unclear: Whether the "per-scan, not cached at startup" clause the roadmap references was already removed.
   - Recommendation: Planner should read REQUIREMENTS.md verbatim before drafting edit tasks.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | NUnit 4.x (via `TelegramGroupsAdmin.UnitTests`) |
| Config file | No separate config — TestFixture + SetUp/TearDown attributes |
| Quick run command | `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --no-build --filter "FullyQualifiedName~ClamAVScannerServiceTests"` |
| Full suite command | `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --no-build` |

### Phase Requirements to Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CLAM-01 | `CLAMAV__HOST`+`CLAMAV__PORT` activates override, DB not called | unit | `dotnet test ... --filter "FullyQualifiedName~ClamAVScannerServiceTests"` | Already exists — update 2 string constants |
| CLAM-02 | Partial override uses DB config | unit | same filter | Already exists — update 2 string constants |
| CLAM-03 | First call logs INFO, second does not | unit | same filter | Already exists — update 2 string constants |
| CLAM-04 | GetHealthAsync uses same override path | unit | same filter | Already exists — update 2 string constants |

### Sampling Rate

- **Per task commit:** `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --no-build --filter "FullyQualifiedName~ClamAVScannerServiceTests"`
- **Per wave merge:** `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --no-build`
- **Phase gate:** Full unit suite green before `/gsd:verify-work`

### Wave 0 Gaps

None — existing test infrastructure covers all phase requirements. Only string constant updates required, no new test files.

---

## Sources

### Primary (HIGH confidence)

- Direct code inspection: `ClamAVScannerService.cs` — full implementation read
- Direct code inspection: `ClamAVScannerServiceTests.cs` — test infrastructure and env var constants confirmed
- Direct code inspection: `ConfigurationExtensions.cs` — `AddApplicationConfiguration` pattern confirmed
- Direct code inspection: `examples/compose.development.yml` and `examples/compose.production.yml` — `CLAMAV__HOST`/`CLAMAV__PORT` double-underscore confirmed
- Direct code inspection: `TelegramGroupsAdmin.ContentDetection/TelegramGroupsAdmin.ContentDetection.csproj` — no `Microsoft.Extensions.Configuration` direct dep; must inject through constructor, not `IConfiguration` directly

### Secondary (MEDIUM confidence)

- ASP.NET Core double-underscore env var convention (`CLAMAV__HOST` → `ClamAV:Host`) — well-established framework feature, confirmed by existing compose pattern matching other env vars (`APP__BASEURL`, `MESSAGEHISTORY__ENABLED`)

---

## Metadata

**Confidence breakdown:**

- Root cause (wrong env var names): HIGH — directly verified by comparing compose env var names vs `Environment.GetEnvironmentVariable` string literals in service
- Fix approach (Option B, two string literals): HIGH — minimal, behavior-preserving, no new dependencies
- Doc changes needed: HIGH — production compose lacks `ENABLE_METRICS`; REQUIREMENTS.md CLAM-01 needs "per-scan" clause removal (verify at plan time)
- Test updates: HIGH — constants drive all tests, changing `EnvHost`/`EnvPort` constants updates all 9 tests

**Research date:** 2026-03-19
**Valid until:** 2026-04-19 (stable .NET/ASP.NET Core patterns, no external dependencies)
