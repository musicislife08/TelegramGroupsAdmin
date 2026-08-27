# Ban Celebration Live Bag Insert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A ban celebration GIF or caption added through the settings UI joins the active shuffle rotation immediately, instead of waiting for the in-memory shuffle bag to drain.

**Architecture:** `BanCelebrationCache` is a singleton holding one `Queue<int>` of IDs per content type; `BanCelebrationService` refills a queue from the database only when it runs dry. Two new cache methods splice a single new ID into the *remaining* items of a live queue at a random position, preserving the "everything shown once before any repeat" guarantee. The two repositories call them right after they create a row, so every add path — UI upload and URL download — is covered without touching the UI.

**Tech Stack:** .NET 10, C#, NUnit, NSubstitute 6, EF Core 10 with `IDbContextFactory<AppDbContext>`, Testcontainers-backed PostgreSQL for integration tests.

**Spec:** `docs/superpowers/specs/2026-08-26-ban-celebration-live-bag-insert-design.md`

## Global Constraints

- Branch is already created: `feat/ban-celebration-live-bag-insert`. Never commit to `master` or `develop`.
- Conventional commit prefixes (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`).
- NSubstitute 6 matcher lambdas are nullable-annotated: `Arg.Is<T>(x => x!.Prop == y)` needs the null-forgiving `!` on the first dereference. Never `?.` — it makes a null argument silently compare `false` instead of throwing.
- No UI changes in this plan. `BanCelebrationSettings.razor` and the add dialogs stay exactly as they are.
- Solution file is `TelegramGroupsAdmin.sln`.
- Integration tests require Docker to be running (Testcontainers spins up PostgreSQL per test).

---

### Task 1: Cache splice methods

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/IBanCelebrationCache.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCelebrationCache.cs`
- Create: `TelegramGroupsAdmin.UnitTests/Services/BanCelebrationCacheTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `void IBanCelebrationCache.AddGifId(int id)` and `void IBanCelebrationCache.AddCaptionId(int id)`. Both return void, are thread-safe, and are no-ops when their bag is empty. Tasks 2 and 3 call them.

- [ ] **Step 1: Write the failing tests**

Create `TelegramGroupsAdmin.UnitTests/Services/BanCelebrationCacheTests.cs`:

```csharp
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for BanCelebrationCache shuffle-bag behavior, focused on
/// AddGifId/AddCaptionId splicing new library items into a live bag.
/// </summary>
[TestFixture]
public class BanCelebrationCacheTests
{
    private BanCelebrationCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _cache = new BanCelebrationCache();
    }

    /// <summary>Drains a bag into a list, so a whole cycle can be asserted on.</summary>
    private List<int> DrainGifBag()
    {
        var drained = new List<int>();
        while (_cache.GetNextGifId() is { } id)
        {
            drained.Add(id);
        }

        return drained;
    }

    private List<int> DrainCaptionBag()
    {
        var drained = new List<int>();
        while (_cache.GetNextCaptionId() is { } id)
        {
            drained.Add(id);
        }

        return drained;
    }

    [Test]
    public void AddGifId_BagHasPendingItems_NewIdJoinsCurrentCycleExactlyOnce()
    {
        _cache.RepopulateGifBag([1, 2, 3, 4]);
        _cache.GetNextGifId(); // dispense one, leaving three pending

        _cache.AddGifId(99);

        var remaining = DrainGifBag();
        Assert.Multiple(() =>
        {
            Assert.That(remaining, Has.Count.EqualTo(4), "three pending items plus the new one");
            Assert.That(remaining.Count(id => id == 99), Is.EqualTo(1), "new ID appears exactly once");
            Assert.That(remaining, Is.Unique, "no item is duplicated by the splice");
        });
    }

    [Test]
    public void AddGifId_BagHasPendingItems_AlreadyDispensedItemDoesNotReturn()
    {
        _cache.RepopulateGifBag([1, 2, 3, 4]);
        var dispensed = _cache.GetNextGifId()!.Value;

        _cache.AddGifId(99);

        Assert.That(DrainGifBag(), Does.Not.Contain(dispensed),
            "splicing must not restore an item that was already shown this cycle");
    }

    [Test]
    public void AddGifId_EmptyBag_IsNoOpSoNextPullReloadsFromDatabase()
    {
        _cache.AddGifId(99);

        Assert.Multiple(() =>
        {
            Assert.That(_cache.IsGifBagEmpty, Is.True);
            Assert.That(_cache.GetNextGifId(), Is.Null);
        });
    }

    [Test]
    public void AddCaptionId_BagHasPendingItems_NewIdJoinsCurrentCycleExactlyOnce()
    {
        _cache.RepopulateCaptionBag([1, 2, 3, 4]);
        _cache.GetNextCaptionId();

        _cache.AddCaptionId(99);

        var remaining = DrainCaptionBag();
        Assert.Multiple(() =>
        {
            Assert.That(remaining, Has.Count.EqualTo(4));
            Assert.That(remaining.Count(id => id == 99), Is.EqualTo(1));
            Assert.That(remaining, Is.Unique);
        });
    }

    [Test]
    public void AddCaptionId_EmptyBag_IsNoOpSoNextPullReloadsFromDatabase()
    {
        _cache.AddCaptionId(99);

        Assert.Multiple(() =>
        {
            Assert.That(_cache.IsCaptionBagEmpty, Is.True);
            Assert.That(_cache.GetNextCaptionId(), Is.Null);
        });
    }

    [Test]
    public void AddGifId_DoesNotTouchCaptionBag()
    {
        _cache.RepopulateCaptionBag([1, 2]);

        _cache.AddGifId(99);

        Assert.That(DrainCaptionBag(), Is.EquivalentTo(new[] { 1, 2 }));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~BanCelebrationCacheTests`
Expected: build failure — `'IBanCelebrationCache' does not contain a definition for 'AddGifId'` (and `AddCaptionId`).

- [ ] **Step 3: Add the interface members**

In `TelegramGroupsAdmin.Telegram/Services/IBanCelebrationCache.cs`, add after `RepopulateGifBag`:

```csharp
    /// <summary>
    /// Splices a newly added GIF ID into the pending items of the current bag at a
    /// random position, so new library content joins the rotation immediately.
    /// No-op when the bag is empty: the next pull reloads every ID from the database,
    /// which already includes the new one.
    /// </summary>
    void AddGifId(int id);
```

and after `RepopulateCaptionBag`:

```csharp
    /// <summary>
    /// Splices a newly added caption ID into the pending items of the current bag at a
    /// random position. Same semantics as <see cref="AddGifId"/>.
    /// </summary>
    void AddCaptionId(int id);
```

- [ ] **Step 4: Implement the splice in the cache**

In `TelegramGroupsAdmin.Telegram/Services/BanCelebrationCache.cs`, add after `RepopulateGifBag`:

```csharp
    public void AddGifId(int id)
    {
        lock (_gifLock)
        {
            // Empty bag means the next pull reloads all IDs from the database,
            // which will include this one. Inserting here would instead make the
            // new item jump the queue and force an immediate reshuffle after it.
            if (_gifBag.Count == 0)
                return;

            SpliceInto(_gifBag, id);
        }
    }
```

and after `RepopulateCaptionBag`:

```csharp
    public void AddCaptionId(int id)
    {
        lock (_captionLock)
        {
            if (_captionBag.Count == 0)
                return;

            SpliceInto(_captionBag, id);
        }
    }
```

and this private helper at the end of the class:

```csharp
    /// <summary>
    /// Inserts an ID at a uniformly random position among the queue's pending items.
    /// Queue&lt;int&gt; has no random insert, so the pending items are drained to a list,
    /// the ID is inserted, and the list is re-enqueued in order. Bags hold at most the
    /// library size (tens of items), so the copy is negligible.
    /// Callers must already hold the matching lock.
    /// </summary>
    private static void SpliceInto(Queue<int> bag, int id)
    {
        var pending = new List<int>(bag);
        bag.Clear();

        pending.Insert(Random.Shared.Next(pending.Count + 1), id);

        foreach (var pendingId in pending)
        {
            bag.Enqueue(pendingId);
        }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~BanCelebrationCacheTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/IBanCelebrationCache.cs \
        TelegramGroupsAdmin.Telegram/Services/BanCelebrationCache.cs \
        TelegramGroupsAdmin.UnitTests/Services/BanCelebrationCacheTests.cs
git commit -m "feat(celebration): splice new IDs into the live shuffle bag"
```

---

### Task 2: GIF repository notifies the cache

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationGifRepository.cs` (constructor at line 29, end of `AddFromFileAsync` around line 199)
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationGifRepositoryTests.cs`

**Interfaces:**
- Consumes: `IBanCelebrationCache.AddGifId(int id)` from Task 1.
- Produces: `BanCelebrationGifRepository`'s constructor gains a sixth parameter, `IBanCelebrationCache celebrationCache`, appended after `ILogger<BanCelebrationGifRepository> logger`. No interface change — `IBanCelebrationGifRepository` is untouched, so DI registration in `ServiceCollectionExtensions` needs no edit.

- [ ] **Step 1: Write the failing test**

In `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationGifRepositoryTests.cs`, add a field beside the other mocks:

```csharp
    private IBanCelebrationCache _mockCelebrationCache = null!;
```

Register it in `SetUp`, immediately before `_serviceProvider = services.BuildServiceProvider();`:

```csharp
        // Cache is notified when a GIF is added so it joins the live shuffle bag
        _mockCelebrationCache = Substitute.For<IBanCelebrationCache>();
        services.AddSingleton(_mockCelebrationCache);
```

Add `using TelegramGroupsAdmin.Telegram.Services;` to the file's usings if it is not already there. Then add this test (place it beside the other `AddFromFileAsync` tests):

```csharp
    [Test]
    public async Task AddFromFileAsync_ValidGif_NotifiesShuffleBagWithNewId()
    {
        // Arrange
        using var stream = CreateTestGifStream();

        // Act
        var result = await _repository!.AddFromFileAsync(stream, "new.gif", "New GIF");

        // Assert
        _mockCelebrationCache.Received(1).AddGifId(result.Id);
    }
```

`CreateTestGifStream()` is the fixture's existing private helper — the same one the other `AddFromFileAsync` tests use. Do not add a new helper.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebrationGifRepositoryTests.AddFromFileAsync_ValidGif_NotifiesShuffleBagWithNewId`
Expected: FAIL — `Expected to receive exactly 1 call matching: AddGifId(...), actually received no matching calls`.

- [ ] **Step 3: Inject the cache and call it**

In `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationGifRepository.cs`, add the field beside `_httpClient`:

```csharp
    private readonly IBanCelebrationCache _celebrationCache;
```

Add the constructor parameter and assignment:

```csharp
    public BanCelebrationGifRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        IVideoFrameExtractionService videoService,
        IOptions<AppOptions> appOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<BanCelebrationGifRepository> logger,
        IBanCelebrationCache celebrationCache)
    {
        _contextFactory = contextFactory;
        _videoService = videoService;
        _logger = logger;
        _celebrationCache = celebrationCache;
```

(leave the rest of the constructor body unchanged), and add `using TelegramGroupsAdmin.Telegram.Services;` to the usings.

At the very end of `AddFromFileAsync`, after the existing `_logger.LogInformation("Added ban celebration GIF: ...")` call and immediately before `return dto.ToModel();`:

```csharp
        // Join the live shuffle bag so the new GIF can be picked before the bag drains.
        // Must be last: the row is created up front to obtain an ID, and the video
        // conversion failure paths above delete it again.
        _celebrationCache.AddGifId(dto.Id);

        return dto.ToModel();
```

Do not touch `AddFromUrlAsync` — it ends by delegating to `AddFromFileAsync`, so it is already covered.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebrationGifRepositoryTests`
Expected: PASS, including the pre-existing tests in the fixture.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationGifRepository.cs \
        TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationGifRepositoryTests.cs
git commit -m "feat(celebration): add new GIFs to the shuffle bag on upload"
```

---

### Task 3: Caption repository notifies the cache

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationCaptionRepository.cs` (constructor at line 19, `AddAsync` at line 64)
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationCaptionRepositoryTests.cs`

**Interfaces:**
- Consumes: `IBanCelebrationCache.AddCaptionId(int id)` from Task 1.
- Produces: `BanCelebrationCaptionRepository`'s constructor gains a third parameter, `IBanCelebrationCache celebrationCache`, appended after `ILogger<BanCelebrationCaptionRepository> logger`. `IBanCelebrationCaptionRepository` is untouched.

- [ ] **Step 1: Write the failing test**

In `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationCaptionRepositoryTests.cs`, add a field beside `_repository`:

```csharp
    private IBanCelebrationCache _mockCelebrationCache = null!;
```

Register it in `SetUp`, immediately before `_serviceProvider = services.BuildServiceProvider();`:

```csharp
        // Cache is notified when a caption is added so it joins the live shuffle bag
        _mockCelebrationCache = Substitute.For<IBanCelebrationCache>();
        services.AddSingleton(_mockCelebrationCache);
```

Add these usings to the file: `using NSubstitute;` and `using TelegramGroupsAdmin.Telegram.Services;`. Then add this test beside the other `AddAsync` tests:

```csharp
    [Test]
    public async Task AddAsync_ValidCaption_NotifiesShuffleBagWithNewId()
    {
        // Act
        var result = await _repository!.AddAsync("{username} was banned!", "You were banned!", "Test");

        // Assert
        _mockCelebrationCache.Received(1).AddCaptionId(result.Id);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebrationCaptionRepositoryTests.AddAsync_ValidCaption_NotifiesShuffleBagWithNewId`
Expected: FAIL — `Expected to receive exactly 1 call matching: AddCaptionId(...), actually received no matching calls`.

- [ ] **Step 3: Inject the cache and call it**

In `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationCaptionRepository.cs`, add `using TelegramGroupsAdmin.Telegram.Services;` and change the field block plus constructor to:

```csharp
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<BanCelebrationCaptionRepository> _logger;
    private readonly IBanCelebrationCache _celebrationCache;

    public BanCelebrationCaptionRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<BanCelebrationCaptionRepository> logger,
        IBanCelebrationCache celebrationCache)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _celebrationCache = celebrationCache;
    }
```

In `AddAsync`, after the existing `_logger.LogInformation("Added ban celebration caption: ...")` call and immediately before `return dto.ToModel();`:

```csharp
        // Join the live shuffle bag so the new caption can be picked before the bag drains.
        _celebrationCache.AddCaptionId(dto.Id);

        return dto.ToModel();
```

Leave `SeedDefaultsIfEmptyAsync` alone: it only runs against an empty library, where the bag is empty and the next pull loads the seeded rows anyway.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebrationCaptionRepositoryTests`
Expected: PASS, including the pre-existing tests in the fixture.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationCaptionRepository.cs \
        TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationCaptionRepositoryTests.cs
git commit -m "feat(celebration): add new captions to the shuffle bag on create"
```

---

### Task 4: Full build and test sweep

**Files:**
- Modify: any construction site of the two repositories that the compiler flags (unit tests, component tests, or other fixtures that `new` them directly rather than resolving from DI).

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: a green build and test run for the whole solution.

- [ ] **Step 1: Build the solution and fix any broken construction sites**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: PASS. If any file fails with "no overload takes N arguments" for either repository, that call site constructs the repository directly — pass `Substitute.For<IBanCelebrationCache>()` as the new last argument (test code) or resolve it from DI (production code). Do not change any behavior beyond satisfying the new parameter.

- [ ] **Step 2: Run the unit and component test projects**

Run: `dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.ComponentTests`
Expected: PASS. Report any pre-existing failure rather than fixing unrelated tests.

- [ ] **Step 3: Run the integration tests for both repositories and the celebration service**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebration`
Expected: PASS. Requires Docker.

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "test: update ban celebration repository construction for cache dependency"
```

Skip this commit if Steps 1–3 needed no changes.

---

## Verification

The behavior this plan delivers, stated as the check a reviewer can run by hand: with the app running and at least two GIFs in the library, trigger a ban so the bag fills and one GIF is dispensed, add a new GIF in Settings → Ban Celebration, then trigger bans until the cycle completes. The new GIF appears within that same cycle rather than after it.
