# #407 GetOrCreate / TagDefinitions TOCTOU — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert three racy read-then-write repository methods to atomic PostgreSQL `ON CONFLICT` statements, matching the existing `UpsertAsync` precedent in the same file.

**Architecture:** All three methods use `context.Database.ExecuteSqlAsync(FormattableString)` for the write. `GetOrCreateAsync` and `TagDefinitions.CreateAsync` use `INSERT … ON CONFLICT DO NOTHING` followed by an `AsNoTracking` `FirstAsync`. `IncrementUsageAsync` uses `INSERT (… usage_count=1 …) ON CONFLICT DO UPDATE SET usage_count = … + 1` as a single atomic statement.

**Tech Stack:** EF Core 10, Npgsql, PostgreSQL 18, NUnit, Testcontainers.PostgreSQL (existing integration-test infrastructure).

**Spec:** `docs/superpowers/specs/2026-05-27-bug-407-getorcreate-toctou-design.md`

---

## File Structure

- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs:43-86` — `GetOrCreateAsync`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs:43-73` — `CreateAsync`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs:125-155` — `IncrementUsageAsync`
- Create or extend: `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryGetOrCreateRaceTests.cs`
- Create or extend: `TelegramGroupsAdmin.IntegrationTests/Repositories/TagDefinitionsRepositoryRaceTests.cs`

---

## Task 1: Integration test for `GetOrCreateAsync` concurrent insert race

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryGetOrCreateRaceTests.cs`

- [ ] **Step 1: Write the failing race test**

```csharp
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

[TestFixture]
public class TelegramUserRepositoryGetOrCreateRaceTests : IntegrationTestBase
{
    [Test]
    public async Task GetOrCreateAsync_ConcurrentCallsSameId_BothSucceed_OneRowExists()
    {
        var repo = ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var contextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var id = 9_876_543_210L;
        var identity = new UserIdentity(id, "race", null, "racer");

        var task1 = Task.Run(() => repo.GetOrCreateAsync(identity, isBot: false, CancellationToken.None));
        var task2 = Task.Run(() => repo.GetOrCreateAsync(identity, isBot: false, CancellationToken.None));

        var results = await Task.WhenAll(task1, task2);

        Assert.That(results[0].TelegramUserId, Is.EqualTo(id));
        Assert.That(results[1].TelegramUserId, Is.EqualTo(id));

        await using var ctx = await contextFactory.CreateDbContextAsync();
        var count = await ctx.TelegramUsers.CountAsync(u => u.TelegramUserId == id);
        Assert.That(count, Is.EqualTo(1));
    }
}
```

(Replace `IntegrationTestBase` and `ServiceProvider` accessors with whatever the existing integration tests in this project use — see `TelegramUserRepositoryTests.cs` for the pattern.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GetOrCreateAsync_ConcurrentCallsSameId_BothSucceed_OneRowExists"`

Expected: FAIL — one task throws `DbUpdateException` (unique violation on `telegram_user_id`).

---

## Task 2: Implement `GetOrCreateAsync` with `ON CONFLICT DO NOTHING`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs:43-86`

- [ ] **Step 1: Replace the read-then-conditional-insert body**

Replace the method body with:

```csharp
public async Task<UiModels.TelegramUser> GetOrCreateAsync(
    UserIdentity user, bool isBot, CancellationToken cancellationToken = default)
{
    await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
    var now = DateTimeOffset.UtcNow;
    var isTrusted = TelegramConstants.IsSystemUser(user.Id);

    await context.Database.ExecuteSqlAsync($"""
        INSERT INTO telegram_users (
            telegram_user_id, username, first_name, last_name,
            is_bot, is_trusted, is_banned, bot_dm_enabled,
            first_seen_at, last_seen_at, created_at, updated_at, is_active
        ) VALUES (
            {user.Id}, {user.Username}, {user.FirstName}, {user.LastName},
            {isBot}, {isTrusted}, {false}, {false},
            {now}, {now}, {now}, {now}, {false}
        )
        ON CONFLICT (telegram_user_id) DO NOTHING
        """, cancellationToken);

    var entity = await context.TelegramUsers
        .AsNoTracking()
        .FirstAsync(u => u.TelegramUserId == user.Id, cancellationToken);

    return entity.ToModel();
}
```

- [ ] **Step 2: Run race test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GetOrCreateAsync_ConcurrentCallsSameId_BothSucceed_OneRowExists"`

Expected: PASS

- [ ] **Step 3: Run the existing `GetOrCreateAsync` tests in `TelegramUserRepositoryTests`**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TelegramUserRepositoryTests"`

Expected: PASS — preserves the existing "insert on miss, return as-is on hit (no field mutation)" semantic. If any existing test asserted on the trust-account log line, update it (it's been removed since insert-vs-hit is no longer detectable without `RETURNING xmax = 0`).

---

## Task 3: Integration test for `TagDefinitionsRepository.CreateAsync` concurrent insert race

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Repositories/TagDefinitionsRepositoryRaceTests.cs`

- [ ] **Step 1: Write the failing race test**

```csharp
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

[TestFixture]
public class TagDefinitionsRepositoryRaceTests : IntegrationTestBase
{
    [Test]
    public async Task CreateAsync_ConcurrentCallsSameTag_BothSucceed_OneRowExists()
    {
        var repo = ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
        var contextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var tagName = $"race-tag-{Guid.NewGuid():N}";

        var task1 = Task.Run(() => repo.CreateAsync(tagName, TagColor.Primary, CancellationToken.None));
        var task2 = Task.Run(() => repo.CreateAsync(tagName, TagColor.Primary, CancellationToken.None));

        var results = await Task.WhenAll(task1, task2);

        Assert.That(results[0].TagName, Is.EqualTo(tagName));
        Assert.That(results[1].TagName, Is.EqualTo(tagName));

        await using var ctx = await contextFactory.CreateDbContextAsync();
        var count = await ctx.TagDefinitions.CountAsync(t => t.TagName == tagName);
        Assert.That(count, Is.EqualTo(1));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~CreateAsync_ConcurrentCallsSameTag_BothSucceed_OneRowExists"`

Expected: FAIL — one task throws `DbUpdateException`.

---

## Task 4: Implement `TagDefinitions.CreateAsync` with `ON CONFLICT DO NOTHING`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs:43-73`

- [ ] **Step 1: Replace method body**

```csharp
public async Task<TagDefinition> CreateAsync(string tagName, Models.TagColor color, CancellationToken cancellationToken = default)
{
    var normalizedTag = tagName.Trim().ToLowerInvariant();
    var now = DateTimeOffset.UtcNow;
    var dataColor = (int)(Data.Models.TagColor)color;

    await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

    await context.Database.ExecuteSqlAsync($"""
        INSERT INTO tag_definitions (tag_name, color, usage_count, created_at)
        VALUES ({normalizedTag}, {dataColor}, {0}, {now})
        ON CONFLICT (tag_name) DO NOTHING
        """, cancellationToken);

    var definition = await context.TagDefinitions
        .AsNoTracking()
        .FirstAsync(td => td.TagName == normalizedTag, cancellationToken);

    _logger.LogInformation("Ensured tag definition exists: {TagName}", normalizedTag);
    return definition.ToModel();
}
```

The `"Tag definition already exists"` warning log is replaced with the `"Ensured tag definition exists"` info log — the new log fires unconditionally because we can no longer distinguish insert-vs-hit without an extra query.

- [ ] **Step 2: Run race test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~CreateAsync_ConcurrentCallsSameTag_BothSucceed_OneRowExists"`

Expected: PASS

- [ ] **Step 3: Run existing TagDefinitions tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TagDefinitionsRepository"`

Expected: PASS — update any assertion on the old "already exists" log if present.

---

## Task 5: Integration test for `IncrementUsageAsync` lost-increment race

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Repositories/TagDefinitionsRepositoryRaceTests.cs`

- [ ] **Step 1: Write the failing lost-increment test**

```csharp
[Test]
public async Task IncrementUsageAsync_ConcurrentCalls_FinalCountEqualsCallCount()
{
    var repo = ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
    var contextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    var tagName = $"inc-race-{Guid.NewGuid():N}";

    // Pre-create with usage_count = 0
    await repo.CreateAsync(tagName, TagColor.Primary, CancellationToken.None);

    const int concurrentCalls = 20;
    var tasks = Enumerable.Range(0, concurrentCalls)
        .Select(_ => Task.Run(() => repo.IncrementUsageAsync(tagName, CancellationToken.None)))
        .ToArray();

    await Task.WhenAll(tasks);

    await using var ctx = await contextFactory.CreateDbContextAsync();
    var def = await ctx.TagDefinitions.AsNoTracking().FirstAsync(t => t.TagName == tagName);
    Assert.That(def.UsageCount, Is.EqualTo(concurrentCalls));
}

[Test]
public async Task IncrementUsageAsync_ConcurrentCallsOnNewTag_BothSucceed_OneRowFinalCountIsCallCount()
{
    var repo = ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
    var contextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    var tagName = $"new-inc-race-{Guid.NewGuid():N}";

    var task1 = Task.Run(() => repo.IncrementUsageAsync(tagName, CancellationToken.None));
    var task2 = Task.Run(() => repo.IncrementUsageAsync(tagName, CancellationToken.None));
    await Task.WhenAll(task1, task2);

    await using var ctx = await contextFactory.CreateDbContextAsync();
    var def = await ctx.TagDefinitions.AsNoTracking().FirstAsync(t => t.TagName == tagName);
    Assert.That(def.UsageCount, Is.EqualTo(2));

    var count = await ctx.TagDefinitions.CountAsync(t => t.TagName == tagName);
    Assert.That(count, Is.EqualTo(1));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~IncrementUsageAsync_ConcurrentCalls"`

Expected: FAIL — final `UsageCount` < `concurrentCalls` due to lost updates, OR one task throws `DbUpdateException` in the new-tag case.

---

## Task 6: Implement `IncrementUsageAsync` with `ON CONFLICT DO UPDATE SET usage_count + 1`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs:125-155`

- [ ] **Step 1: Replace method body**

```csharp
public async Task IncrementUsageAsync(string tagName, CancellationToken cancellationToken = default)
{
    var normalizedTag = tagName.ToLowerInvariant();
    var now = DateTimeOffset.UtcNow;
    var primaryColor = (int)Data.Models.TagColor.Primary;

    await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

    await context.Database.ExecuteSqlAsync($"""
        INSERT INTO tag_definitions (tag_name, color, usage_count, created_at)
        VALUES ({normalizedTag}, {primaryColor}, {1}, {now})
        ON CONFLICT (tag_name) DO UPDATE SET usage_count = tag_definitions.usage_count + 1
        """, cancellationToken);
}
```

The `"Auto-created tag definition"` log line is removed — we no longer distinguish insert-vs-update without an extra query, and the event isn't load-bearing for any consumer.

- [ ] **Step 2: Run lost-increment test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~IncrementUsageAsync_ConcurrentCalls_FinalCountEqualsCallCount"`

Expected: PASS — final count equals concurrent call count.

- [ ] **Step 3: Run new-tag race test**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~IncrementUsageAsync_ConcurrentCallsOnNewTag_BothSucceed_OneRowFinalCountIsCallCount"`

Expected: PASS — single row exists, usage_count = 2.

- [ ] **Step 4: Run all TagDefinitions tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TagDefinitionsRepository"`

Expected: PASS. If any prior test asserted on the `"Auto-created tag definition"` log line, remove or update that assertion.

---

## Task 7: Final verification + commit

- [ ] **Step 1: Run the full integration suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests`

Expected: all tests pass.

- [ ] **Step 2: Run unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests`

Expected: all tests pass. If any unit test mocked `IDbContextFactory` and asserted on the old read-check-insert sequence, update it (the new shape calls `Database.ExecuteSqlAsync` once, then `FirstAsync`).

- [ ] **Step 3: Build the full solution**

Run: `dotnet build`

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs \
        TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs \
        TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryGetOrCreateRaceTests.cs \
        TelegramGroupsAdmin.IntegrationTests/Repositories/TagDefinitionsRepositoryRaceTests.cs

git commit -m "$(cat <<'EOF'
fix(repos): ON CONFLICT for GetOrCreate / TagDefinitions TOCTOU races

Closes #407.

Three repository methods (TelegramUserRepository.GetOrCreateAsync,
TagDefinitionsRepository.CreateAsync, TagDefinitionsRepository.IncrementUsageAsync)
switch from read-then-write to ExecuteSqlAsync with ON CONFLICT clauses,
matching the existing UpsertAsync precedent. Eliminates DbUpdateException
under concurrent IDbContextFactory access and the lost-increment race in
the usage counter.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```
