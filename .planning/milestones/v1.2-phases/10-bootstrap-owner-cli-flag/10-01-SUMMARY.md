---
phase: 10-bootstrap-owner-cli-flag
plan: 01
subsystem: auth
tags: [bootstrap, cli, owner, tdd, nsubstitute]

# Dependency graph
requires: []
provides:
  - AnyUsersExistAsync method on IUserRepository (DB-first idempotency check)
  - BootstrapOwnerService static class with ExecuteAsync method
  - BootstrapCredentials sealed record for JSON deserialization
  - --bootstrap CLI flag wired into Program.cs (after --restore, before ML training)
  - Actor.Bootstrap static readonly field with "CLI Bootstrap" display name
  - IsFirstRunAsync optimization via AnyUsersExistAsync (stops at first row)
affects: [phase-11, any code calling IUserRepository, any code using --bootstrap flag]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "CLI flag pattern: SkipWhile arg parsing, using var scope, GetRequiredService, Environment.Exit"
    - "TDD RED/GREEN: stub throws NotImplementedException, then full implementation replaces it"
    - "Non-fatal audit: try/catch around LogEventAsync with LogWarning on failure"
    - "DB-first idempotency: AnyAsync beats CountAsync == 0 for bootstrap guard"

key-files:
  created:
    - TelegramGroupsAdmin/Models/BootstrapCredentials.cs
    - TelegramGroupsAdmin/Services/BootstrapOwnerService.cs
    - TelegramGroupsAdmin.UnitTests/Services/Auth/BootstrapOwnerServiceTests.cs
  modified:
    - TelegramGroupsAdmin/Repositories/IUserRepository.cs
    - TelegramGroupsAdmin/Repositories/UserRepository.cs
    - TelegramGroupsAdmin/Services/AuthService.cs
    - TelegramGroupsAdmin.Core/Models/Actor.cs
    - TelegramGroupsAdmin/Program.cs

key-decisions:
  - "AnyUsersExistAsync uses AnyAsync (stops at first row) rather than CountAsync for efficiency in bootstrap idempotency check"
  - "BootstrapOwnerService is a static class to enable testability without DI — Program.cs calls it with resolved services directly"
  - "EmailVerified=true unconditionally on bootstrap (no featureAvailability check) — K8s init container must produce a ready-to-login account"
  - "TotpEnabled=true + TotpSecret=null — requires TOTP setup on first login, consistent with all other Owner creation paths"

patterns-established:
  - "Bootstrap CLI: --arg parsing via SkipWhile().Skip(1).FirstOrDefault(), using var scope (not await using), Environment.Exit inside using block"
  - "Non-fatal audit: always wrap LogEventAsync in try/catch, log warning on failure, continue with success result"

requirements-completed: [BOOT-01, BOOT-02, BOOT-03, BOOT-04, BOOT-05, BOOT-06, BOOT-07]

# Metrics
duration: 10min
completed: 2026-03-19
---

# Phase 10 Plan 01: Bootstrap Owner CLI Flag Summary

**--bootstrap CLI flag with BootstrapOwnerService: idempotent Owner account creation from JSON credentials file, wired into Program.cs between --restore and ML training**

## Performance

- **Duration:** 10 min
- **Started:** 2026-03-19T04:46:00Z
- **Completed:** 2026-03-19T04:56:22Z
- **Tasks:** 2 (TDD: RED + GREEN)
- **Files modified:** 8

## Accomplishments

- Added `AnyUsersExistAsync` to `IUserRepository` and `UserRepository` (DB-first idempotency, AnyAsync stops at first row)
- Updated `AuthService.IsFirstRunAsync` to use `AnyUsersExistAsync` (more efficient than CountAsync == 0)
- Created `BootstrapOwnerService` static class with full `ExecuteAsync` implementation covering all 7 BOOT requirements
- Wired `--bootstrap` flag into `Program.cs` after `--restore` block and before ML training
- 13 NUnit tests covering BOOT-01 through BOOT-06 with NSubstitute mocks; all pass
- 1790 total unit tests pass with zero regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Repository + Actor contracts and bootstrap helper with tests (RED)** - `83c14f90` (test)
2. **Task 2: Implement bootstrap logic and wire into Program.cs (GREEN)** - `2f431cc9` (feat)

_Note: TDD tasks have two commits (RED stub + GREEN implementation)_

## Files Created/Modified

- `TelegramGroupsAdmin/Repositories/IUserRepository.cs` - Added `AnyUsersExistAsync` method declaration
- `TelegramGroupsAdmin/Repositories/UserRepository.cs` - Implemented `AnyUsersExistAsync` using `AnyAsync`
- `TelegramGroupsAdmin/Services/AuthService.cs` - Updated `IsFirstRunAsync` to use `AnyUsersExistAsync`
- `TelegramGroupsAdmin.Core/Models/Actor.cs` - Added `"bootstrap" => "CLI Bootstrap"` to switch + `Bootstrap` static field
- `TelegramGroupsAdmin/Models/BootstrapCredentials.cs` - New sealed record for JSON deserialization target
- `TelegramGroupsAdmin/Services/BootstrapOwnerService.cs` - New static class with full `ExecuteAsync` implementation
- `TelegramGroupsAdmin/Program.cs` - `--bootstrap` block inserted + added usings for IUserRepository, IPasswordHasher, IAuditService
- `TelegramGroupsAdmin.UnitTests/Services/Auth/BootstrapOwnerServiceTests.cs` - 13 NUnit tests covering all BOOT behaviors

## Decisions Made

- `AnyUsersExistAsync` uses `AnyAsync` (stops at first row) rather than `CountAsync` — efficient for the bootstrap idempotency guard
- `BootstrapOwnerService` is a static class, not an injectable service — Program.cs passes resolved services directly, keeps the service layer clean
- `EmailVerified=true` unconditionally on bootstrap (no `featureAvailability` check) — a K8s init container must produce a login-ready account regardless of email service config
- `TotpEnabled=true + TotpSecret=null` on bootstrap — requires TOTP setup on first login, consistent with all other Owner creation paths in the codebase

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required. Bootstrap runs via `dotnet run --bootstrap /path/to/credentials.json`.

## Next Phase Readiness

- BOOT-01 through BOOT-07 satisfied and tested
- `--bootstrap` flag available for K8s init container usage
- Phase 11 (status endpoint) can proceed independently

## Self-Check: PASSED

All files confirmed present, all commits verified in git log.

---
*Phase: 10-bootstrap-owner-cli-flag*
*Completed: 2026-03-19*
