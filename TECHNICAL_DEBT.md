# Refactoring Backlog - TelegramGroupsAdmin

**Last Updated:** 2025-10-21
**Status:** Pre-production (breaking changes acceptable)
**Overall Code Quality:** 88/100 (Excellent)

---

## Completed Optimizations

**2025-10-21**: PERF-APP-1 (Messages N+1 query), PERF-APP-3 (StateHasChanged batching)
**2025-10-19**: Users N+1, config caching, parallel bans, composite index, virtualization, record conversion, leak fix, allocation optimization
**Total**: 12 performance optimizations completed

---

## Performance Optimization Issues

**Deployment Context:** 10+ chats, 1000+ users, 100-1000 messages/day, Messages page primary moderation tool

### Medium Priority (4 issues)

#### PERF-CD-1: Stop Words N+1 (Future-Proofing)

**Project:** TelegramGroupsAdmin.ContentDetection
**Files:** StopWordsRepository.cs (lines 117-158)
**Severity:** Medium | **Impact:** Future-proofing for 10x growth

**Description:**
Loading stop words with actor information uses complex GroupJoin pattern that could result in N+1 queries. Currently only 20 stop words, so no performance issue, but this future-proofs for growth to 200+ words.

**Reality Check:** At 20 stop words, loading takes ~40-60ms (perfectly acceptable). However, you don't know how many stop words could be added by you or other admins over time. This optimization prepares for 10x growth scenario.

**Current Pattern:**
Multiple JOINs for actor resolution executed per stop word in projection query.

**Recommended Fix:**
```csharp
// Pre-load all actors in single query
var actorIds = await context.StopWords
    .SelectMany(s => new[] { s.AddedBy, s.UpdatedBy }.Where(id => id != 0))
    .Distinct()
    .ToListAsync(cancellationToken);

// Batch load actors (assuming you add GetActorsByIdsAsync method)
var actors = await GetActorsByIdsAsync(actorIds, cancellationToken);
var actorDict = actors.ToDictionary(a => a.Id);

// Single query for stop words, then client-side actor join
var stopWords = await context.StopWords
    .AsNoTracking()
    .OrderByDescending(s => s.AddedAt)
    .ToListAsync(cancellationToken);

return stopWords.Select(s => new StopWordWithEmailDto
{
    // ... fields ...
    AddedByActor = actorDict.GetValueOrDefault(s.AddedBy),
    UpdatedByActor = s.UpdatedBy.HasValue ? actorDict.GetValueOrDefault(s.UpdatedBy.Value) : null
}).ToList();
```

**Expected Gain:** None at current scale (20 words), but prevents 40ms → 500ms degradation if growing to 200+ words

---

#### PERF-CD-3: Analytics Page - Batch Domain Filter Stats

**Project:** TelegramGroupsAdmin.ContentDetection
**Files:** CachedBlockedDomainsRepository.cs (lines 111-136)
**Severity:** Medium | **Impact:** 85% faster analytics stats queries

**Description:**
GetStatsAsync() executes 8 separate CountAsync queries sequentially for domain statistics. If analytics page has multiple stat sections, this compounds to 20-30+ queries.

**Reality Check:** Analytics page viewed less frequently than Messages/Users pages, but at 1000+ users with URL filtering active, stats queries aggregate large datasets. Single GroupBy query is the standard analytics pattern.

**Current Pattern:**
```csharp
var globalCount = await context.CachedBlockedDomains.CountAsync(d => d.ChatId == null);
var chat1Count = await context.CachedBlockedDomains.CountAsync(d => d.ChatId == chat1Id);
// ... 6 more sequential counts
```

**Recommended Fix:**
```csharp
var stats = await context.CachedBlockedDomains
    .GroupBy(d => d.ChatId)
    .Select(g => new { ChatId = g.Key, Count = g.Count() })
    .ToListAsync(cancellationToken);

return new UrlFilterStats
{
    GlobalCount = stats.FirstOrDefault(s => s.ChatId == null)?.Count ?? 0,
    PerChatCounts = stats.Where(s => s.ChatId != null).ToDictionary(s => s.ChatId.Value, s => s.Count)
};
```

**Expected Gain:** 85% faster (8 queries → 1, 150ms → 20ms), worth doing during analytics development

---

#### PERF-DATA-5: JSON Source Generation for Config JSONB

**Project:** TelegramGroupsAdmin.Data
**Files:** ConfigRepository.cs, various JSONB columns
**Severity:** Medium | **Impact:** 30% faster JSON operations

**Description:**
Configuration and JSONB columns use reflection-based JSON deserialization instead of source-generated serializers. With caching (PERF-CFG-1), this becomes less critical, but still valuable for initial loads.

**Recommended Fix:**
```csharp
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SpamDetectionConfig))]
[JsonSerializable(typeof(WelcomeConfig))]
[JsonSerializable(typeof(BotProtectionConfig))]
// ... all config types
internal partial class ConfigJsonContext : JsonSerializerContext { }

// Usage in ConfigService:
var config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SpamDetectionConfig);
```

**Expected Gain:** 30% faster JSON operations, better AOT compatibility, complements config caching

---

#### PERF-CD-4: TF-IDF Vector Calculation Optimization

**Project:** TelegramGroupsAdmin.ContentDetection
**Files:** SimilaritySpamCheck.cs
**Severity:** Medium | **Impact:** 40% faster similarity checks

**Description:**
TF-IDF calculation uses LINQ Count() in nested loops, causing repeated enumerations. At 10-50 spam checks/day (new users only), this isn't critical, but cleaner implementation is straightforward.

**Reality Check:** With spam checks only on new users (~10-50/day), similarity check performance isn't a bottleneck. However, pre-computed term frequencies are the standard approach for TF-IDF.

**Recommended Fix:**
Use pre-computed term frequencies with Dictionary lookups instead of repeated LINQ Count() calls.

**Expected Gain:** 40% faster TF-IDF calculation (100ms → 60ms per check), negligible user-visible impact at current check frequency

---

## Performance Optimization Summary

**Deployment Context:** 10+ chats, 100-1000 messages/day (10-50 spam checks/day on new users), 1000+ users, Messages page primary tool

**Total Issues Remaining:** 4 medium priority (down from 52 initial findings, 38 false positives removed)

**Implementation Priority:** Implement opportunistically during related refactoring work

---

### DI-1: Interface-Only Dependency Injection Audit

**Date Added:** 2025-10-20
**Status:** PENDING ⏳
**Severity:** Best Practice | **Impact:** Testability, maintainability, proper DI patterns

**Description:**
Audit all dependency injection usage across the codebase to ensure interfaces are used instead of concrete types, unless there's a specific reason to inject concrete types (e.g., framework types, sealed types).

**Progress So Far:**
1. Created interfaces for 4 repositories that were missing them:
   - `IAuditLogRepository` (5 methods)
   - `IUserRepository` (24 methods)
   - `IMessageHistoryRepository` (20 methods)
   - `ITelegramUserRepository` (15 methods)
2. Updated DI registrations for these 4 repositories to use interface-to-implementation mappings
3. Systematically replaced concrete type injections for these 4 repositories using automated find/replace
4. Verified runtime with `dotnet run --migrate-only` - all DI errors for these repositories resolved

**Remaining Work:**
- Audit all other services, repositories, and dependencies to verify they use interfaces
- Check for any remaining concrete type injections (HttpClient, framework types are acceptable)
- Verify all DI registrations follow `services.AddScoped<IFoo, Foo>()` pattern
- Document any exceptions where concrete types are intentionally injected

**Goal:**
Ensure all application services inject interfaces only. This enables:
- Better testability (can mock dependencies)
- Clearer contracts between layers
- Compile-time detection of missing DI registrations
- Proper separation of concerns

**Future Consideration:**
When adding new repositories or services, always create an interface first and register with DI using `services.AddScoped<IFoo, Foo>()` pattern.

---

### ARCH-1: Move Shared Code to Core Library

**Date Added:** 2025-10-21
**Audit Completed:** 2025-10-21
**Phase 1 Completed:** 2025-10-21
**Phase 2 Completed:** 2025-10-21
**Phase 3 Completed:** 2025-10-21
**Status:** COMPLETE ✅
**Severity:** Architecture | **Impact:** Eliminated 544-574 lines duplicated code, broke circular dependencies

**Final Core Status:**
TelegramGroupsAdmin.Core fully established as shared utilities layer. All HIGH, MEDIUM, and Phase 3 items complete.

**HIGH PRIORITY (Move Immediately)** ⚠️

1. ✅ **URL Extraction (4-way duplication)** - COMPLETED 2025-10-21
   - **Files**: UrlPreFilterService.cs, MessageProcessingService.cs, SeoScrapingSpamCheck.cs, UrlBlocklistSpamCheck.cs
   - **Libraries**: ContentDetection, Telegram, SpamDetection (2x)
   - **Issue**: Same regex URL extraction logic duplicated 4 times
   - **Moved to**: Core.Utilities.UrlUtilities.ExtractUrls()
   - **Impact**: Eliminated ~50-60 lines of duplicated code, bug fixes only need applying once

2. ✅ **Duration Parsing (Exact 44-line duplicate)** - COMPLETED 2025-10-21
   - **Files**: TempBanCommand.cs, MuteCommand.cs
   - **Library**: Telegram
   - **Issue**: Identical ParseDuration() and FormatDuration() methods copied between commands
   - **Moved to**: Core.Utilities.TimeSpanUtilities (TryParseDuration() + FormatDuration())
   - **Impact**: Eliminated 88 lines total (44 per command), maintenance burden eliminated

**MEDIUM PRIORITY (Move During Related Work)**

3. ✅ **TickerQHelper** - COMPLETED 2025-10-21
   - **File**: TickerQHelper.cs
   - **Library**: Telegram → Core
   - **Used by**: MessageProcessingService, ModerationActionService, WelcomeService, DmDeliveryService
   - **Issue**: Created coupling - Multiple services depended on Telegram.Helpers
   - **Moved to**: Core.BackgroundJobs.TickerQUtilities
   - **Impact**: Eliminated 116 lines, broke circular dependency risk, updated 4 services

4. ✅ **SHA256 Hashing (Duplication)** - COMPLETED 2025-10-21
   - **Files**: FileScanJob.cs, MessageProcessingService.cs
   - **Library**: Telegram
   - **Issue**: Duplicated hash computation logic (file and content hashing)
   - **Moved to**: Core.Utilities.HashUtilities (ComputeSHA256Async + ComputeSHA256)
   - **Impact**: Eliminated ~10 lines duplication, consistent hashing implementation

**PHASE 3 (Final Telegram Audit)** 🎯

5. ✅ **FormatDuration Duplicate** - COMPLETED 2025-10-21
   - **File**: ModerationActionService.cs
   - **Library**: Telegram
   - **Issue**: Private duplicate of TimeSpanUtilities.FormatDuration()
   - **Moved to**: Core.Utilities.TimeSpanUtilities (already existed)
   - **Impact**: Eliminated 15 lines duplicate code

6. ✅ **Bitwise Operations** - COMPLETED 2025-10-21
   - **File**: PhotoHashService.cs
   - **Library**: Telegram → Core
   - **Issue**: Generic bitwise operations (HammingDistance, PopCount)
   - **Moved to**: Core.Utilities.BitwiseUtilities
   - **Impact**: Eliminated 22 lines, reusable for checksums/integrity verification

7. ✅ **String Utilities** - COMPLETED 2025-10-21
   - **Files**: ImpersonationDetectionService.cs
   - **Library**: Telegram → Core
   - **Issue**: Generic string algorithms (Levenshtein distance, name similarity)
   - **Moved to**: Core.Utilities.StringUtilities
   - **Impact**: Eliminated 49 lines, reusable for fuzzy matching/search

8. ✅ **Content Hash Extension** - COMPLETED 2025-10-21
   - **File**: MessageProcessingService.cs
   - **Library**: Telegram → Core
   - **Issue**: Message content hashing logic
   - **Moved to**: Core.Utilities.HashUtilities.ComputeContentHash()
   - **Impact**: Eliminated 4 lines, consistent hash normalization

9. ✅ **Actor.ParseLegacyFormat** - COMPLETED 2025-10-21
   - **File**: ModerationActionService.cs
   - **Library**: Telegram → Core
   - **Issue**: ConvertToActor logic duplicated, Actor belongs in Core
   - **Moved to**: Core.Models.Actor.ParseLegacyFormat()
   - **Impact**: Eliminated 44 lines, documented ARCH-2 for future refactoring

10. ✅ **PhotoHashService** - COMPLETED 2025-10-21
    - **Files**: PhotoHashService.cs, IPhotoHashService.cs
    - **Library**: Telegram → Core.Services
    - **Used by**: FetchUserPhotoJob (main app), ImpersonationDetectionService
    - **Issue**: Main app job uses it, should be in Core
    - **Moved to**: Core.Services.PhotoHashService
    - **Impact**: Eliminated ~150 lines duplication, proper layering

**Final Core Structure:**
```
TelegramGroupsAdmin.Core/
  Models/
    Actor.cs (✅ exists, extended Phase 3)
  Utilities/
    UrlUtilities.cs (✅ created Phase 1)
    TimeSpanUtilities.cs (✅ created Phase 1)
    HashUtilities.cs (✅ created Phase 2, extended Phase 3)
    BitwiseUtilities.cs (✅ created Phase 3)
    StringUtilities.cs (✅ created Phase 3)
  Services/
    IPhotoHashService.cs (✅ moved Phase 3)
    PhotoHashService.cs (✅ moved Phase 3)
  BackgroundJobs/
    TickerQUtilities.cs (✅ moved Phase 2)
```

**Implementation Complete:**
- Phase 1 (HIGH): UrlUtilities, TimeSpanUtilities - ~138-148 lines eliminated
- Phase 2 (MEDIUM): TickerQUtilities, HashUtilities - ~126 lines eliminated
- Phase 3 (Final Audit): BitwiseUtilities, StringUtilities, PhotoHashService, Actor.ParseLegacyFormat - ~284 lines eliminated
- **Total**: 544-574 lines of duplicated code eliminated

**Packages Added to Core:**
- Microsoft.Extensions.DependencyInjection.Abstractions 9.0.10
- Microsoft.Extensions.Logging.Abstractions 9.0.10
- TickerQ 2.5.3
- SixLabors.ImageSharp 3.1.11

---

### ARCH-2: Eliminate Actor String Round-Tripping

**Date Added:** 2025-10-21
**Status:** PENDING ⏳
**Severity:** Architecture | **Impact:** Cleaner moderation API, eliminates unnecessary string conversions

**Current Architecture:**
Bot commands and web services create string-based executor IDs, pass them to `ModerationActionService`, which then parses them back into `Actor` objects using `Actor.ParseLegacyFormat()`.

**The Problem:**
```
BotCommand → GetExecutorIdentifierAsync() → string ID ("telegram:123456789")
          → BanUserAsync(string executorId)
          → Actor.ParseLegacyFormat(executorId) → Actor object
```

This is unnecessary round-tripping. We convert user info → string → Actor when we could go directly from user info → Actor.

**Files Affected:**
- `ModerationActionService.cs`: All moderation methods (BanUserAsync, WarnUserAsync, RestrictUserAsync, TempBanUserAsync)
- `GetExecutorIdentifierAsync()`: Creates string IDs from Telegram user info
- All callers: BotCommands (BanCommand, WarnCommand, MuteCommand, TempBanCommand), ReportActionsService, ImpersonationDetectionService

**Current String Formats:**
- Web users: GUID strings (`"550e8400-e29b-41d4-a716-446655440000"`)
- Telegram users: `"telegram:@username"` or `"telegram:123456789"`
- System: `"system:identifier"`

**Proposed Refactoring:**

1. **Change moderation method signatures** to accept `Actor` instead of `string executorId`:
   ```csharp
   // Before
   Task<ModerationResult> BanUserAsync(..., string executorId, ...)

   // After
   Task<ModerationResult> BanUserAsync(..., Actor executor, ...)
   ```

2. **Update bot commands** to create `Actor` objects directly:
   ```csharp
   // Before
   var executorId = await _moderationService.GetExecutorIdentifierAsync(
       message.From!.Id, message.From.Username, cancellationToken);
   await _moderationService.BanUserAsync(..., executorId, ...);

   // After
   var executor = Actor.FromTelegramUser(
       message.From!.Id, message.From.Username, message.From.FirstName);
   await _moderationService.BanUserAsync(..., executor, ...);
   ```

3. **Remove `GetExecutorIdentifierAsync()`** - no longer needed

4. **Keep `Actor.ParseLegacyFormat()`** for:
   - Database migration scripts (if needed)
   - API backward compatibility (if exposing to external systems)
   - Can be moved out of hot path

**Benefits:**
- Eliminates 2 string conversion operations per moderation action
- Clearer API (Actor objects are self-documenting)
- Type safety (can't pass malformed executor strings)
- Removes 50+ lines of string manipulation code

**Estimated Effort:** 2-3 hours
- Update 4 moderation method signatures
- Update ~10 call sites (bot commands + services)
- Remove GetExecutorIdentifierAsync helper
- Test all moderation flows

**Priority:** Medium - Architecture cleanup, no functional impact

---

## Notes

- **Pre-production status:** Breaking changes are acceptable
- **Readability-first:** Modern features used only when clarity improves
- **No feature changes:** Pure refactoring, preserve all functionality
- **Build quality:** Must maintain 0 errors, 0 warnings standard

**Last Updated:** 2025-10-21
**Next Review:** When implementing medium priority optimizations opportunistically
