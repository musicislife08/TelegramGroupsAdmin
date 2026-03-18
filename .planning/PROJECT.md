# TelegramGroupsAdmin

## What This Is

A Telegram group administration bot with Blazor Server UI, managing content detection, moderation, user tracking, analytics, and background jobs for Telegram groups. Deployed as a single-instance homelab application with PostgreSQL backend.

## Core Value

Reliable, automated Telegram group moderation with a responsive web UI for configuration and monitoring — correctness and operational simplicity above all.

## Current Milestone: v1.1 Bug Fix Sweep

**Goal:** Fix 7 known bugs spanning analytics, health checks, database access patterns, startup warnings, media handling, race conditions, and timezone rendering.

**Target fixes:**
- #384 — Analytics overview card percentages don't update with time range selection
- #333 — Health orchestrator missing wiring (CheckHealthAsync and MarkInactiveAsync unused)
- #326 — Migrate remaining repositories from scoped AppDbContext to IDbContextFactory
- #309 — Address three spurious warnings in startup/runtime logs
- #262 — Download media when marking spam to populate image_training_samples
- #204 — Potential race condition in TelegramUserRepository.UpsertAsync
- #203 — Timezone detection JS interop fails during Blazor prerendering

## Requirements

### Validated

- ✓ Existing Telegram bot polling, Blazor UI, content detection, background jobs, and all runtime features remain fully functional — v1.0
- ✓ ~30 dead files removed across Core, Configuration, Data, Telegram, ContentDetection, and main app projects — v1.0
- ✓ 22 dead methods removed from live interfaces and their implementations — v1.0
- ✓ Dead enum value ScanResultType.Suspicious removed — v1.0
- ✓ Orphaned tests removed that only tested deleted code — v1.0

### Active

- [ ] Analytics overview card percentages update correctly when time range changes (#384)
- [ ] Health orchestrator CheckHealthAsync and MarkInactiveAsync are wired up and functional (#333)
- [ ] All repositories use IDbContextFactory instead of scoped AppDbContext (#326)
- [ ] Three spurious startup/runtime warnings are resolved (#309)
- [ ] Media is downloaded when marking spam to populate image_training_samples (#262)
- [ ] TelegramUserRepository.UpsertAsync race condition is fixed (#204)
- [ ] Timezone detection JS interop handles Blazor prerendering gracefully (#203)

### Out of Scope

- IFileScanResultRepository.CleanupExpiredResultsAsync wiring — #398 (enhancement, not bug)
- IBlocklistSubscriptionsRepository.FindByUrlAsync wiring — #399 (enhancement, not bug)
- ContentCheckMetadata population — #400 (enhancement, not bug)
- FileScanQuotaModel computed properties investigation — #401 (tech-debt, not bug)
- ConfigRecord.cs + WelcomeConfigMappings.cs design violation — #342 (refactoring)
- Enum columns stored as varchar instead of smallint — #403 (refactoring)
- New features, enhancements, and refactoring — deferred to future milestones

## Context

- Brownfield: mature codebase with established layered architecture (Data -> Core -> Telegram/ContentDetection -> Main)
- v1.0 dead code removal completed: -636 lines, 62 items removed, 1747 unit tests passing
- These 7 bugs were identified during development and tracked as GitHub issues
- #326 (IDbContextFactory) affects background services where scoped DbContext can cause disposed-context exceptions
- #204 (race condition) is a data integrity risk in concurrent upsert scenarios

## Constraints

- **Zero regression**: All fixes must preserve existing behavior — only fix the reported bug
- **Build must pass**: Solution compiled and all tests pass after each phase
- **Git workflow**: Feature branch off develop, PR to develop per project rules
- **Single instance**: Telegram Bot API enforces one active connection per bot token

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Bug-only milestone | Clear backlog of correctness issues before adding features | — Pending |
| Skip research phase | All bugs are well-documented in GitHub issues, no domain research needed | — Pending |
| Group by layer/domain | Bugs span DB, backend, frontend — group phases by affected layer | — Pending |

---
*Last updated: 2026-03-16 after v1.1 milestone start*
