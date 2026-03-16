# Dead Code Removal (Issue #396)

## What This Is

A comprehensive dead code cleanup of the TelegramGroupsAdmin solution. 62 confirmed dead items were identified by a multi-agent audit with cross-project verification. This project removes all of them — files, methods, properties, DI registrations, enum values, stale comments, and orphaned tests.

## Core Value

Reduce codebase surface area by removing all confirmed dead code, making the solution easier to maintain and navigate without changing any runtime behavior.

## Requirements

### Validated

- Existing Telegram bot polling, Blazor UI, content detection, background jobs, and all runtime features remain fully functional after cleanup

### Active

- [ ] Remove ~30 dead files across Core, Configuration, Data, Telegram, ContentDetection, and main app projects
- [ ] Remove 2 dead DI registrations (IMediaNotificationService, AddHttpClient)
- [ ] Remove 18 dead methods from live interfaces and their implementations
- [ ] Remove 4 dead properties from ContentCheckRequest and ImageCheckRequest
- [ ] Remove dead enum value ScanResultType.Suspicious
- [ ] Fix 2 stale comments (misleading "Deprecated" and transition comment)
- [ ] Remove orphaned tests that only test deleted code

### Out of Scope

- IFileScanResultRepository.CleanupExpiredResultsAsync wiring — #398
- IBlocklistSubscriptionsRepository.FindByUrlAsync wiring — #399
- ContentCheckMetadata population — #400
- FileScanQuotaModel computed properties investigation — #401
- ConfigRecord.cs + WelcomeConfigMappings.cs design violation — #342
- Any new functionality or behavioral changes

## Context

- Brownfield: mature codebase with established layered architecture (Data -> Core -> Telegram/ContentDetection -> Main)
- Dead code was identified by 5 collaborative agents (4 hunters + 1 cross-project verifier) with 10.1% false positive rejection rate
- All findings independently cross-verified against DI registration, Blazor @inject, Quartz job registration, and reflection-based usage
- Detailed item-by-item manifest in GitHub issue #396

## Constraints

- **Zero behavior change**: All removals are pure deletions — no functional changes
- **Build must pass**: Solution must compile and all remaining tests must pass after each phase
- **Git workflow**: Feature branch off develop, PR to develop per project rules
- **Single PR**: All cleanup in one branch/PR that closes #396

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Remove all 62 items in one pass | Issue is well-verified, no partial cleanup needed | -- Pending |
| Delete orphaned tests with their dead code | Tests that only test dead code are themselves dead | -- Pending |
| Exclude 5 related issues | Those require wiring/design work, not just deletion | -- Pending |

---
*Last updated: 2026-03-16 after initialization*
