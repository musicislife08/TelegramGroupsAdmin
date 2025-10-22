# Refactoring Backlog - TelegramGroupsAdmin

**Last Updated:** 2025-10-21
**Status:** Pre-production (breaking changes acceptable)
**Overall Code Quality:** 88/100 (Excellent)

---

## Completed Work

**2025-10-21**: ARCH-1 (Core library - 544 lines eliminated), PERF-APP-1, PERF-APP-3, DI-1 (4 repositories)
**2025-10-19**: 8 performance optimizations (Users N+1, config caching, parallel bans, composite index, virtualization, record conversion, leak fix, allocation optimization)

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

**Status:** PENDING ⏳
**Severity:** Best Practice | **Impact:** Testability, maintainability

**Remaining Work:**
- Audit all services/repositories to verify they use interfaces (HttpClient/framework types OK)
- Verify all DI registrations follow `services.AddScoped<IFoo, Foo>()` pattern
- Document exceptions where concrete types are intentionally injected

**Progress:** Created interfaces for 4 repositories (IAuditLogRepository, IUserRepository, IMessageHistoryRepository, ITelegramUserRepository), updated DI registrations, verified runtime

---

### ARCH-2: Eliminate Actor String Round-Tripping

**Status:** PENDING ⏳
**Severity:** Architecture | **Impact:** Cleaner API, type safety, removes 50+ lines

**Problem:**
Bot commands create string executor IDs → ModerationActionService → Actor.ParseLegacyFormat() → Actor object. Unnecessary round-tripping (user info → string → Actor).

**Refactoring:**
1. Change moderation method signatures to accept `Actor` instead of `string executorId`
2. Update bot commands to create `Actor` objects directly (use `Actor.FromTelegramUser()`)
3. Remove `GetExecutorIdentifierAsync()` method
4. Keep `Actor.ParseLegacyFormat()` for backward compatibility only

**Files:** ModerationActionService.cs, BotCommands (Ban/Warn/Mute/TempBan), ReportActionsService, ImpersonationDetectionService

**Estimated Effort:** 2-3 hours

---

## Notes

- **Pre-production status:** Breaking changes are acceptable
- **Readability-first:** Modern features used only when clarity improves
- **No feature changes:** Pure refactoring, preserve all functionality
- **Build quality:** Must maintain 0 errors, 0 warnings standard

**Last Updated:** 2025-10-21
**Next Review:** When implementing medium priority optimizations opportunistically
