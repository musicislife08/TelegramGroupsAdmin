# TelegramGroupsAdmin

## What This Is

A Telegram group administration bot with Blazor Server UI, managing content detection, moderation, user tracking, analytics, and background jobs for Telegram groups. Deployed as a single-instance homelab application with PostgreSQL backend.

## Core Value

Reliable, automated Telegram group moderation with a responsive web UI for configuration and monitoring — correctness and operational simplicity above all.

## Requirements

### Validated

- ✓ Existing Telegram bot polling, Blazor UI, content detection, background jobs, and all runtime features remain fully functional — v1.0
- ✓ ~30 dead files removed across Core, Configuration, Data, Telegram, ContentDetection, and main app projects — v1.0
- ✓ 22 dead methods removed from live interfaces and their implementations — v1.0
- ✓ Dead enum value ScanResultType.Suspicious removed — v1.0
- ✓ Orphaned tests removed that only tested deleted code — v1.0
- ✓ Analytics overview card percentages update correctly when time range changes (#384) — v1.1
- ✓ Health orchestrator CheckHealthAsync and MarkInactiveAsync are wired up and functional (#333) — v1.1
- ✓ All repositories use IDbContextFactory instead of scoped AppDbContext (#326) — v1.1
- ✓ Three spurious startup/runtime warnings are resolved (#309) — v1.1
- ✓ Media is downloaded when marking spam to populate image_training_samples (#262) — v1.1
- ✓ TelegramUserRepository.UpsertAsync race condition is fixed (#204) — v1.1
- ✓ Timezone detection JS interop handles Blazor prerendering gracefully (#203) — v1.1

### Active

(None — planning next milestone)

### Out of Scope

- IFileScanResultRepository.CleanupExpiredResultsAsync wiring — #398 (enhancement, not bug)
- IBlocklistSubscriptionsRepository.FindByUrlAsync wiring — #399 (enhancement, not bug)
- ContentCheckMetadata population — #400 (enhancement, not bug)
- FileScanQuotaModel computed properties investigation — #401 (tech-debt, not bug)
- ConfigRecord.cs + WelcomeConfigMappings.cs design violation — #342 (refactoring)
- Enum columns stored as varchar instead of smallint — #403 (refactoring)

## Context

- Brownfield: mature codebase with established layered architecture (Data -> Core -> Telegram/ContentDetection -> Main)
- v1.0 dead code removal completed: -636 lines, 62 items removed
- v1.1 bug fix sweep completed: 7 correctness bugs fixed, +9,926/-3,435 lines across 174 files
- All repositories now use IDbContextFactory for safe lifetime management
- UpsertAsync uses atomic PostgreSQL ON CONFLICT DO UPDATE
- Timezone detection centralized in MainLayout cascade
- Analytics growth metrics decoupled from date range selection

## Constraints

- **Zero regression**: All fixes must preserve existing behavior
- **Build must pass**: Solution compiled and all tests pass after each phase
- **Git workflow**: Feature branch off develop, PR to develop per project rules
- **Single instance**: Telegram Bot API enforces one active connection per bot token

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Bug-only milestone (v1.1) | Clear backlog of correctness issues before adding features | ✓ Good — 7 bugs fixed in 3 days |
| Skip research phase | All bugs are well-documented in GitHub issues, no domain research needed | ✓ Good — saved time |
| Group by layer/domain | Bugs span DB, backend, frontend — group phases by affected layer | ✓ Good — clean separation |
| Atomic upsert via ON CONFLICT | Replace read-then-write with single SQL statement | ✓ Good — eliminates race condition |
| Timezone cascade from MainLayout | Single detection point, cascaded TimeZoneInfo to all children | ✓ Good — eliminates JSException during prerender |
| Replace DailyAverageGrowthPercent with PreviousDailyAverage | Growth % was algebraically identical to MessageGrowthPercent | ✓ Good — shows meaningful comparison |

---
*Last updated: 2026-03-18 after v1.1 milestone*
