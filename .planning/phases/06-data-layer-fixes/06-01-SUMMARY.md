---
phase: 06-data-layer-fixes
plan: "01"
subsystem: data-layer
tags: [repositories, efcore, idbcontextfactory, postgres, upsert, concurrency]
dependency_graph:
  requires: []
  provides: [safe-dbcontext-lifetime, atomic-user-upsert]
  affects: [background-services, message-processing, content-detection]
tech_stack:
  added: []
  patterns: [IDbContextFactory per-method context, PostgreSQL ON CONFLICT DO UPDATE, FormattableString ExecuteSqlAsync]
key_files:
  created: []
  modified:
    - TelegramGroupsAdmin.ContentDetection/Repositories/BlocklistSubscriptionsRepository.cs
    - TelegramGroupsAdmin.ContentDetection/Repositories/DomainFiltersRepository.cs
    - TelegramGroupsAdmin.ContentDetection/Repositories/CachedBlockedDomainsRepository.cs
    - TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs
    - TelegramGroupsAdmin.Telegram/Repositories/PendingNotificationsRepository.cs
    - TelegramGroupsAdmin.Telegram/Repositories/AdminNotesRepository.cs
    - TelegramGroupsAdmin.Telegram/Repositories/UserTagsRepository.cs
    - TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs
decisions:
  - "IDbContextFactory per-method context: each async method creates and disposes its own DbContext, preventing ObjectDisposedException in background services where the scope outlives the HTTP request"
  - "ExecuteSqlAsync with FormattableString raw string literal: auto-parameterizes all {expr} interpolations, preventing SQL injection while keeping readable SQL syntax"
  - "is_trusted and bot_dm_enabled excluded from ON CONFLICT UPDATE SET: admin-controlled fields must not be overwritten by message processing; they are preserved on upsert conflict"
  - "is_active hardcoded to true on conflict: a user sending a message definitively makes them active regardless of what the caller passes in"
metrics:
  duration: "5m 54s"
  completed_date: "2026-03-17"
  tasks_completed: 2
  files_modified: 8
---

# Phase 06 Plan 01: Data Layer IDbContextFactory Migration Summary

IDbContextFactory migration for 7 repositories and atomic PostgreSQL ON CONFLICT DO UPDATE upsert for TelegramUserRepository, fixing ObjectDisposedException in background services (DATA-01) and race conditions in concurrent user upserts (DATA-02).

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Migrate 7 repos from AppDbContext to IDbContextFactory | 9837d8ad | 7 repository files |
| 2 | Rewrite UpsertAsync with PostgreSQL ON CONFLICT DO UPDATE | 52d73242 | TelegramUserRepository.cs |

## What Was Built

### Task 1: IDbContextFactory Migration

All 7 repositories were mechanically transformed:

- Constructor parameter changed from `AppDbContext context` to `IDbContextFactory<AppDbContext> contextFactory`
- Field changed from `private readonly AppDbContext _context` to `private readonly IDbContextFactory<AppDbContext> _contextFactory`
- Every async method now begins with `await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken)`
- All `_context.` usages replaced with `context.` (local variable)

**Special cases handled:**
- `UserTagsRepository`: kept `ITagDefinitionsRepository` second constructor parameter
- `TagDefinitionsRepository`: kept `ILogger<TagDefinitionsRepository>` second constructor parameter
- `CachedBlockedDomainsRepository.BulkInsertAsync`: `await using var context` placed immediately before `ExecuteSqlRawAsync` call (after in-memory deduplication work)
- `CachedBlockedDomainsRepository.GetStatsAsync`: single context used across all 3 DbSet accesses (CachedBlockedDomains, BlocklistSubscriptions, DomainFilters) within one method

### Task 2: Atomic UpsertAsync

Replaced the read-then-write pattern in `TelegramUserRepository.UpsertAsync` with:

```sql
INSERT INTO telegram_users (
    telegram_user_id, username, first_name, last_name,
    user_photo_path, photo_hash, is_active, is_trusted,
    is_bot, is_banned, bot_dm_enabled,
    first_seen_at, last_seen_at, created_at, updated_at
) VALUES (...)
ON CONFLICT (telegram_user_id) DO UPDATE SET
    username = EXCLUDED.username,
    first_name = EXCLUDED.first_name,
    last_name = EXCLUDED.last_name,
    user_photo_path = EXCLUDED.user_photo_path,
    photo_hash = EXCLUDED.photo_hash,
    is_active = true,
    last_seen_at = EXCLUDED.last_seen_at,
    updated_at = {now}
```

- Used `ExecuteSqlAsync($"""...""")` (FormattableString overload) — all `{expr}` expressions become parameterized SQL parameters
- `is_trusted` is set via `TelegramConstants.IsSystemUser()` check only on INSERT — never updated on conflict
- `bot_dm_enabled` starts as `false` on INSERT — never updated on conflict
- `is_active` is hardcoded `false` on INSERT, hardcoded `true` on conflict UPDATE

## Verification

1. `dotnet build` — 0 errors, 0 warnings
2. No `private readonly AppDbContext _context` fields in any of the 8 target repos
3. All 7 migrated repos contain at least 2 occurrences of `IDbContextFactory<AppDbContext>`
4. `ON CONFLICT (telegram_user_id) DO UPDATE SET` present in TelegramUserRepository
5. `is_trusted` and `bot_dm_enabled` appear only in the INSERT column list, NOT in the UPDATE SET clause

## Deviations from Plan

None — plan executed exactly as written.

## Self-Check

**PASSED**

All 8 modified files exist on disk. Both task commits (9837d8ad, 52d73242) exist in git log.
