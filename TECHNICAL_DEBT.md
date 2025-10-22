# Refactoring Backlog - TelegramGroupsAdmin

**Generated:** 2025-10-15
**Last Updated:** 2025-10-19
**Status:** Pre-production (breaking changes acceptable)
**Scope:** All 5 projects analyzed by dotnet-refactor-advisor agents

---

## Executive Summary

**Overall Code Quality:** 88/100 (Excellent)
## Performance Optimization Issues

The following performance issues were identified by comprehensive analysis across all 7 projects on 2025-10-19, then reviewed and filtered based on actual deployment context.

**Deployment Context:**
- **Scale:** 10+ managed chats, 1000+ users, moderate expansion planned (2-3x growth)
- **Message Volume:** 100-1000 messages/day, but spam checks only run on new users' first messages (~10-50 checks/day)
- **Usage Patterns:** Messages page is primary moderation tool, heavy web UI usage planned
- **False Positives Removed:** Initial analysis identified 52 issues; after review, 30+ were micro-optimizations or incorrect assumptions about usage patterns

Issues are organized by severity and realistic impact for this deployment scale.

### High Priority (1 issue)

#### PERF-APP-3: Excessive StateHasChanged() Calls in Messages.razor

**Project:** TelegramGroupsAdmin (Main App)
**Files:** Messages.razor (multiple locations)
**Severity:** High | **Impact:** 60-180ms render time reduction

**Description:**
The Messages page calls `StateHasChanged()` 12+ times during a single data load operation, causing excessive re-renders and SignalR traffic. With heavy web UI usage planned, this creates laggy user experience.

**Reality Check:** You plan to use the web UI heavily for moderation. These repeated re-renders cause noticeable UI lag during data loading operations.

**Current Pattern:**
```csharp
await LoadMessages();
StateHasChanged();  // Re-render #1
await LoadDetectionHistory();
StateHasChanged();  // Re-render #2
await LoadUserInfo();
StateHasChanged();  // Re-render #3
// ... 9 more unnecessary re-renders
```

**Recommended Fix:**
```csharp
// Batch all operations, single StateHasChanged at end
await Task.WhenAll(
    LoadMessages(),
    LoadDetectionHistory(),
    LoadUserInfo()
);
StateHasChanged();  // Single re-render after all data loaded
```

**Expected Gain:** 60-180ms render time reduction, 120KB less SignalR traffic per page load, much snappier UI

---

### Medium Priority (5 issues)

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

| Priority | Count | Realistic Impact for This Deployment |
|----------|-------|--------------------------------------|
| High | 1 | **Significant improvement** - Snappier UI with batched renders |
| Medium | 4 | **Moderate improvement** - Future-proofs growth, optimizes analytics |

**Total Issues:** 5 actionable (down from 52 initial findings)
**Completed:** 10 optimizations (Users N+1, Messages N+1 with composite model, config caching, parallel bans, composite index, virtualization, record conversion, leak fix, allocation optimization, detection history JOIN)
**Removed:** 38 false positives (micro-optimizations, wrong usage assumptions, rare operations)

**Estimated Performance Gains:**
- **High issues:** Snappier UI with batched renders (60-180ms reduction)
- **Medium issues:** Future-proofing and polish (10x stop word growth, analytics optimization)

**Implementation Priority:**
1. **PERF-APP-3** (High) - StateHasChanged batching (snappier UI for heavy web usage)
2. Medium - Implement opportunistically during related refactoring

**Testing Strategy:**
- Use `dotnet run --migrate-only` to verify database migrations
- Test config caching with Settings page updates (verify instant UI updates)
- Monitor ban performance with multi-chat test scenarios
- Validate Messages page load times with 50+ messages

**False Positives Removed:**
- Permission checks (only 10-50 checks/day, not 200/min)
- Admin refresh parallel (background job, shouldn't overwhelm API)
- ToList() allocations (1-10MB/day, not 2GB)
- Invite link clearing (rare operation)
- Partial indexes (only 10 active chats)
- String allocations in commands (bytes/day savings)

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

## Notes

- **Pre-production status:** Breaking changes are acceptable
- **Readability-first:** Modern features used only when clarity improves
- **No feature changes:** Pure refactoring, preserve all functionality
- **Build quality:** Must maintain 0 errors, 0 warnings standard

**Last Updated:** 2025-10-21
**Performance Analysis:** 2025-10-19 (reviewed and filtered based on deployment context)
**Next Review:** After implementing Critical/High performance issues, or when extracting Telegram library to NuGet (re-evaluate L7 ConfigureAwait), or when adopting FUTURE-1 IDI pattern
