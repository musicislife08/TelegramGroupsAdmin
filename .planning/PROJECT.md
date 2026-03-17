# Dead Code Removal (Issue #396)

## What This Is

A comprehensive dead code cleanup of the TelegramGroupsAdmin solution. 62 confirmed dead items were identified by a multi-agent audit with cross-project verification, then removed across 5 phases.

## Core Value

Reduce codebase surface area by removing all confirmed dead code, making the solution easier to maintain and navigate without changing any runtime behavior.

## Requirements

### Validated

- ✓ Existing Telegram bot polling, Blazor UI, content detection, background jobs, and all runtime features remain fully functional after cleanup
- ✓ ~30 dead files removed across Core, Configuration, Data, Telegram, ContentDetection, and main app projects — v1.0
- ✓ 2 dead DI registrations removed (IMediaNotificationService, AddHttpClient) — v1.0
- ✓ 22 dead methods removed from live interfaces and their implementations — v1.0
- ✓ 4 dead properties removed from ContentCheckRequest and ImageCheckRequest — v1.0
- ✓ Dead enum value ScanResultType.Suspicious removed — v1.0
- ✓ 2 stale comments fixed (misleading "Deprecated" and transition comment) — v1.0
- ✓ Orphaned tests removed that only tested deleted code — v1.0

### Active

(None — milestone complete)

### Out of Scope

- IFileScanResultRepository.CleanupExpiredResultsAsync wiring — #398
- IBlocklistSubscriptionsRepository.FindByUrlAsync wiring — #399
- ContentCheckMetadata population — #400
- FileScanQuotaModel computed properties investigation — #401
- ConfigRecord.cs + WelcomeConfigMappings.cs design violation — #342
- Enum columns stored as varchar instead of smallint — #403

## Context

- Brownfield: mature codebase with established layered architecture (Data -> Core -> Telegram/ContentDetection -> Main)
- Dead code was identified by 5 collaborative agents (4 hunters + 1 cross-project verifier) with 10.1% false positive rejection rate
- All findings independently cross-verified against DI registration, Blazor @inject, Quartz job registration, and reflection-based usage
- Shipped via PR #402 to develop, issue #396 closed
- Net result: -636 lines of production/test code, 1747 unit tests passing

## Constraints

- **Zero behavior change**: All removals were pure deletions — no functional changes
- **Build must pass**: Solution compiled and all remaining tests passed after each phase
- **Git workflow**: Feature branch off develop, PR to develop per project rules
- **Single PR**: All cleanup in one branch/PR that closed #396

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Remove all 62 items in one pass | Issue is well-verified, no partial cleanup needed | ✓ Good |
| Delete orphaned tests with their dead code | Tests that only test dead code are themselves dead | ✓ Good |
| Exclude 5 related issues | Those require wiring/design work, not just deletion | ✓ Good |
| Auto-fix cascading build breaks | Dead method removal cascades to mock setups and set-sites | ✓ Good — caught 3 deviations |
| Skip research phase | Issue #396 was already a complete verified manifest | ✓ Good — saved tokens without losing quality |

---
*Last updated: 2026-03-17 after v1.0 milestone*
