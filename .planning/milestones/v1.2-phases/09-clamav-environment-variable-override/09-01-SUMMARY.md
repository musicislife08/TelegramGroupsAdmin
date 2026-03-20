---
phase: 09-clamav-environment-variable-override
plan: 01
subsystem: infra
tags: [clamav, nclam, environment-variables, file-scanning, tdd, nunit, nsubstitute]

# Dependency graph
requires: []
provides:
  - CLAMAV_HOST and CLAMAV_PORT env var override in ClamAVScannerService
  - GetEffectiveEndpointAsync private helper centralizing endpoint resolution
  - One-time INFO log per process when override is active (_hasLoggedOverride static)
  - GetHealthAsync and ScanFileAsync both use effective endpoint in all log/error paths
  - 9 unit tests covering CLAM-01 through CLAM-04 via NUnit + NSubstitute
affects: [phase-10, phase-11, content-detection, clamav-operator-deployment]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Static volatile bool for one-time-per-process log guard in Scoped service
    - Sentinel-throw pattern in tests to verify DB consulted without TCP connection wait
    - localhost:1 for fast TCP failure in ClamAV health check tests (connection refused vs timeout)

key-files:
  created:
    - TelegramGroupsAdmin.UnitTests/ContentDetection/ClamAVScannerServiceTests.cs
  modified:
    - TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs

key-decisions:
  - "Static volatile bool _hasLoggedOverride for one-time log: service is Scoped so instance-level field would not survive request scope boundaries"
  - "GetEffectiveEndpointAsync moved inside try/catch in GetHealthAsync so host:port is available in exception error result"
  - "localhost:1 used in CLAM-01/03/04 tests (connection refused = fast) instead of unresolvable hostname (DNS timeout = slow)"
  - "Sentinel-throw pattern in CLAM-02 tests: GetAsync throws after setting flag, proving DB was consulted without hanging on TCP"

patterns-established:
  - "GetEffectiveEndpointAsync pattern: env var check first (both non-whitespace + valid int), fallback to DB config"
  - "Test sentinel: make mock throw after setting a boolean flag to verify a call happened without side effects"

requirements-completed: [CLAM-01, CLAM-02, CLAM-03, CLAM-04]

# Metrics
duration: 15min
completed: 2026-03-18
---

# Phase 9 Plan 01: ClamAV Env Var Override Summary

**CLAMAV_HOST / CLAMAV_PORT env var override in ClamAVScannerService with GetEffectiveEndpointAsync helper, static once-per-process log guard, and 9 NUnit tests covering all four CLAM requirements**

## Performance

- **Duration:** 15 min
- **Started:** 2026-03-18T19:29:05Z
- **Completed:** 2026-03-18T19:44:42Z
- **Tasks:** 1 (TDD: RED + GREEN)
- **Files modified:** 2

## Accomplishments

- `GetEffectiveEndpointAsync` private helper: reads `CLAMAV_HOST` + `CLAMAV_PORT` env vars; both must be non-null/non-whitespace and PORT must parse as valid int for override to apply; otherwise falls back to DB config
- `static volatile bool _hasLoggedOverride` guards a one-time INFO log "ClamAV env var override active" per process (static required because the service is registered as Scoped)
- `GetHealthAsync` refactored to call `GetEffectiveEndpointAsync` inside its try/catch so effective host:port is set in the result for ALL code paths (ping false, ping throws, success)
- `ScanFileAsync` ping-failure error log now uses effective host:port (was previously using stale `config.Tier1.ClamAV.Host/Port`)
- 9 unit tests: CLAM-01 (no DB when both env vars set), CLAM-02 a/b/c/d/e (fallback to DB for each invalid/missing case), CLAM-03 (one-time log), CLAM-04 a/b (result has env var values)

## Task Commits

TDD progression (two commits, one per TDD phase):

1. **RED — failing tests** - `0a307241` (test)
2. **GREEN — implementation** - `5ade9ee3` (feat)

## Files Created/Modified

- `/Users/keisenmenger/Repos/personal/TelegramGroupsAdmin/TelegramGroupsAdmin.UnitTests/ContentDetection/ClamAVScannerServiceTests.cs` - 9 NUnit tests covering CLAM-01 through CLAM-04
- `/Users/keisenmenger/Repos/personal/TelegramGroupsAdmin/TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs` - Added _hasLoggedOverride, GetEffectiveEndpointAsync, refactored CreateClamClientAsync and GetHealthAsync

## Decisions Made

- **localhost:1 for fast TCP failure in tests:** Tests that call `GetHealthAsync` with env var override need ClamClient to fail fast. An unresolvable hostname like "shared-clam" causes multi-second (or multi-minute) DNS + TCP timeout. Using `localhost:1` gives immediate "connection refused" so tests run in <100ms total.

- **Sentinel-throw pattern for CLAM-02:** CLAM-02 fallback tests need to confirm DB was consulted without waiting for TCP. Mock `GetAsync` sets a boolean flag then throws; the exception propagates through `GetEffectiveEndpointAsync` to `GetHealthAsync`'s catch block, returning `IsHealthy = false` immediately. The test asserts the flag was set.

- **GetEffectiveEndpointAsync inside try/catch:** Originally placed the call outside try/catch to ensure `host:port` were available in all catch paths. Refactored to initialize `host = ""` and `port = 0` outside, then assign inside the try block, so exceptions from `GetEffectiveEndpointAsync` are also caught and return a well-formed `FileScannerHealthResult`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] GetHealthAsync exception catch path missing Host/Port in result**

- **Found during:** GREEN implementation, first test run
- **Issue:** Original `GetHealthAsync` had `var (host, port) = await GetEffectiveEndpointAsync(...)` outside the try/catch. The catch block returned `{ IsHealthy = false, ErrorMessage = ex.Message }` with no Host/Port. When ClamClient.PingAsync threw (exception path), result.Host was null/empty regardless of env var values, failing CLAM-04.
- **Fix:** Refactored to declare `host = string.Empty` and `port = 0` outside, call `GetEffectiveEndpointAsync` inside the try block, and include `Host = host, Port = port` in the catch block return value.
- **Files modified:** `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs`
- **Committed in:** `5ade9ee3` (implementation commit)

**2. [Rule 1 - Bug] Tests hung waiting for unresolvable hostname TCP timeout**

- **Found during:** First test run after GREEN implementation
- **Issue:** Tests for CLAM-01/03/04 used `"shared-clam"` as CLAMAV_HOST. DNS resolution + TCP connection to unresolvable host takes 75+ seconds on macOS, making the test suite impractical (stuck for >2 minutes).
- **Fix:** Changed test env var host from `"shared-clam"` to `"localhost"` and port from `"3311"` to `"1"`. `localhost:1` fails immediately with "connection refused" since port 1 is not listening. CLAM-02 tests redesigned to use sentinel-throw pattern (no TCP connection at all).
- **Files modified:** `TelegramGroupsAdmin.UnitTests/ContentDetection/ClamAVScannerServiceTests.cs`
- **Committed in:** `5ade9ee3` (implementation commit)

---

**Total deviations:** 2 auto-fixed (both Rule 1 - bugs found during execution)
**Impact on plan:** Both fixes were necessary for correct behavior and practical test execution. No scope creep.

## Issues Encountered

None beyond the deviations documented above.

## User Setup Required

None - feature is purely env-var activated; no external service configuration required. Operators set `CLAMAV_HOST` and `CLAMAV_PORT` in their container environment to override DB config.

## Next Phase Readiness

- Phase 9 complete: ClamAV env var override fully implemented and tested
- Phase 10 (CLI bootstrap) and Phase 11 (status endpoint) are unblocked
- Operators can now point multiple TGA instances at a shared ClamAV daemon without DB pre-seeding

---
*Phase: 09-clamav-environment-variable-override*
*Completed: 2026-03-18*
