# Correctness Bug Sweep Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix four triaged correctness bugs (#475, #473, #495, #472) on one branch, landing as one PR to `develop` with a commit per issue.

**Architecture:** Each task is independent and self-contained. Order is easiest-to-hardest: an audit-argument fix, an atomic DB UPDATE, observability for swallowed exceptions, and a small DI decoupling. TDD throughout: failing test → minimal change → green → commit.

**Tech Stack:** .NET 10, EF Core 10 (PostgreSQL), NUnit + NSubstitute (unit), real-Postgres integration tests, Quartz.NET hosted service.

**Spec:** `docs/superpowers/specs/2026-05-30-correctness-bug-sweep-design.md`

**Branch:** `fix/correctness-bug-sweep` (already created off `develop`; the spec doc is already committed on it).

---

## File Structure

| File | Task | Responsibility / change |
|---|---|---|
| `TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs` | 1 | 4 mutation methods: pass `target: null` not `actor` |
| `TelegramGroupsAdmin.UnitTests/Telegram/Services/UsernameBlacklistServiceMutationTests.cs` | 1 | 5 positive-path assertions expect a null target |
| `TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs` | 2 | `DecrementUsageAsync` → atomic `ExecuteUpdateAsync` |
| `TelegramGroupsAdmin.IntegrationTests/Repositories/TagDefinitionsRepositoryRaceTests.cs` | 2 | add concurrent-decrement + clamp-at-zero tests |
| `TelegramGroupsAdmin.ContentDetection/Utilities/CheckResultsSerializer.cs` | 3 | remove silent `catch` so `JsonException` propagates |
| `TelegramGroupsAdmin/Repositories/AnalyticsRepository.cs` | 3 | per-call fail-open guard + log |
| `TelegramGroupsAdmin.ContentDetection/ML/StopWordRecommendationService.cs` | 3 | per-row fail-open guard + log |
| `TelegramGroupsAdmin.ContentDetection/Services/VideoFrameExtractionService.cs` | 3 | log before the `128.0` fallback |
| `TelegramGroupsAdmin.UnitTests/ContentDetection/CheckResultsSerializerTests.cs` | 3 | 2 malformed tests flip to expect a throw |
| `TelegramGroupsAdmin.BackgroundJobs/Services/IScheduleResyncSignal.cs` | 4 | **new** — shared wake-up signal abstraction + impl |
| `TelegramGroupsAdmin.BackgroundJobs/Services/QuartzSchedulingSyncService.cs` | 4 | consume the signal; delete cast/field/`TriggerResync` |
| `TelegramGroupsAdmin.BackgroundJobs/Services/BackgroundJobConfigService.cs` | 4 | inject signal; delete `_syncService`/`SetSyncService` |
| `TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs` | 4 | register the signal singleton |
| `TelegramGroupsAdmin.UnitTests/BackgroundJobs/ScheduleResyncSignalTests.cs` | 4 | **new** — unit-test the signal |

**Commands** (run from repo root):
- Build: `dotnet build`
- Unit tests (Debug, local): `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~<ClassName>"`
- Integration tests (real Postgres; run in background with file output per project convention):
  `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~<ClassName>"`

---

## Task 1: #475 — Blacklist audit passes actor as its own target

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs` (4 call sites: ~`:60`, `:75`, `:90`, `:100`)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UsernameBlacklistServiceMutationTests.cs` (5 positive-path assertions)

Note: there are **five** positive-path `Received(1)` assertions, not four — `SetEnabledAsync` has both a True→Enabled and a False→Disabled test. The two `DidNotReceive` tests already use `Arg.Any<Actor?>()` and need no change.

- [ ] **Step 1: Update the test assertions to expect a null target (failing test)**

In `UsernameBlacklistServiceMutationTests.cs`, change the **third argument** (the second `actor`) of each positive-path `Received(1).LogEventAsync(...)` to `Arg.Is<Actor?>(t => t == null)`. There are five:

`AddEntryAsync_CallsRepoThenWritesAuditWithDedicatedEventType` (~line 52):
```csharp
        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryAdded,
            actor,
            Arg.Is<Actor?>(t => t == null),
            "test-pattern",
            Arg.Any<CancellationToken>());
```
`DeleteEntryAsync_WhenRepoReturnsTrue_WritesAudit` (~line 69):
```csharp
        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryRemoved,
            actor, Arg.Is<Actor?>(t => t == null), "test-pattern",
            Arg.Any<CancellationToken>());
```
`SetEnabledAsync_True_WritesEnabledAuditEvent` (~line 98):
```csharp
        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryEnabled,
            actor, Arg.Is<Actor?>(t => t == null), "test-pattern",
            Arg.Any<CancellationToken>());
```
`SetEnabledAsync_False_WritesDisabledAuditEvent` (~line 113):
```csharp
        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryDisabled,
            actor, Arg.Is<Actor?>(t => t == null), "test-pattern",
            Arg.Any<CancellationToken>());
```
`UpdateNotesAsync_WhenRepoReturnsTrue_WritesAudit` (~line 142):
```csharp
        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryNotesChanged,
            actor, Arg.Is<Actor?>(t => t == null), "test-pattern",
            Arg.Any<CancellationToken>());
```

(If the existing event-type argument on any site is currently written differently, keep it as-is — only the target argument changes.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UsernameBlacklistServiceMutationTests"`
Expected: the 5 positive-path tests FAIL (service still passes `actor` as target, so the received call has a non-null target).

- [ ] **Step 3: Fix the four service call sites**

In `UsernameBlacklistService.cs`, change each `LogEventAsync` call to pass `target: null` (fully named optional args for clarity):

`AddEntryAsync`:
```csharp
        await auditService.LogEventAsync(
            AuditEventType.BlacklistEntryAdded,
            actor,
            target: null,
            value: pattern,
            cancellationToken: ct);
```
`DeleteEntryAsync`:
```csharp
            await auditService.LogEventAsync(
                AuditEventType.BlacklistEntryRemoved,
                actor, target: null, value: pattern, cancellationToken: ct);
```
`SetEnabledAsync`:
```csharp
            await auditService.LogEventAsync(eventType, actor, target: null, value: pattern, cancellationToken: ct);
```
`UpdateNotesAsync`:
```csharp
            await auditService.LogEventAsync(
                AuditEventType.BlacklistEntryNotesChanged,
                actor, target: null, value: pattern, cancellationToken: ct);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UsernameBlacklistServiceMutationTests"`
Expected: PASS (all 7 tests: 5 positive + 2 `DidNotReceive`).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/UsernameBlacklistServiceMutationTests.cs
git commit -F- <<'EOF'
fix(audit): pass null target for username blacklist mutations

Blacklist mutations are config events with no distinct user target. All four
mutation methods passed the admin actor into the target slot, producing
misleading audit records and polluting target-based filtering. Pass target:
null, matching the ConfigService convention.

Closes #475
EOF
```

---

## Task 2: #473 — DecrementUsageAsync read-then-write TOCTOU race

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs` — `DecrementUsageAsync` (~`:131-154`)
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/TagDefinitionsRepositoryRaceTests.cs`

These are integration tests against real Postgres. The concurrency test is probabilistic against the OLD code — under contention the lost-update race leaves the count above 0 intermittently; after the fix it is reliably 0.

- [ ] **Step 1: Add the concurrent-decrement and clamp tests (failing test)**

Append these two tests inside the `TagDefinitionsRepositoryRaceTests` class (before the closing brace), mirroring the existing increment-race harness:

```csharp
    [Test]
    public async Task DecrementUsageAsync_ConcurrentCalls_FinalCountClampedAtZero()
    {
        // Arrange — create the tag and raise usage_count to 20
        await using var setupScope = _serviceProvider!.CreateAsyncScope();
        var setupRepo = setupScope.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
        var tagName = $"dec-race-{Guid.NewGuid():N}";
        await setupRepo.CreateAsync(tagName, TagColor.Primary, CancellationToken.None);
        for (var i = 0; i < 20; i++)
        {
            await setupRepo.IncrementUsageAsync(tagName, CancellationToken.None);
        }

        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();

        const int concurrentCalls = 20;
        var tasks = Enumerable.Range(0, concurrentCalls)
            .Select(_ => Task.Run(async () =>
            {
                await using var scope = _serviceProvider!.CreateAsyncScope();
                var scopedRepo = scope.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
                await scopedRepo.DecrementUsageAsync(tagName, CancellationToken.None);
            }))
            .ToArray();

        // Act
        await Task.WhenAll(tasks);

        // Assert — 20 increments minus 20 concurrent decrements, no lost updates
        await using var ctx = await contextFactory.CreateDbContextAsync();
        var def = await ctx.TagDefinitions.AsNoTracking().FirstAsync(t => t.TagName == tagName);
        Assert.That(def.UsageCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DecrementUsageAsync_WhenCountIsZero_StaysAtZero()
    {
        // Arrange — fresh tag, usage_count = 0
        await using var setupScope = _serviceProvider!.CreateAsyncScope();
        var setupRepo = setupScope.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
        var tagName = $"dec-zero-{Guid.NewGuid():N}";
        await setupRepo.CreateAsync(tagName, TagColor.Primary, CancellationToken.None);

        // Act
        await setupRepo.DecrementUsageAsync(tagName, CancellationToken.None);

        // Assert — never goes negative
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await contextFactory.CreateDbContextAsync();
        var def = await ctx.TagDefinitions.AsNoTracking().FirstAsync(t => t.TagName == tagName);
        Assert.That(def.UsageCount, Is.EqualTo(0));
    }
```

- [ ] **Step 2: Run the concurrency test against current code to observe the race**

Run (a few times, since it's probabilistic):
`dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TagDefinitionsRepositoryRaceTests.DecrementUsageAsync_ConcurrentCalls_FinalCountClampedAtZero"`
Expected: intermittent FAIL — final `UsageCount` is greater than 0 (lost updates) on at least one run. (`DecrementUsageAsync_WhenCountIsZero_StaysAtZero` already passes against the old code; that's fine.)

- [ ] **Step 3: Replace DecrementUsageAsync with an atomic ExecuteUpdateAsync**

Replace the entire `DecrementUsageAsync` method body:

```csharp
    public async Task DecrementUsageAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = NormalizeTagName(tagName);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Atomic, clamp-at-zero: the WHERE guard makes a decrement at 0 a no-op,
        // so concurrent callers can never lose an update or drive the count negative.
        await context.TagDefinitions
            .Where(t => t.TagName == normalizedTag && t.UsageCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsageCount, t => t.UsageCount - 1), cancellationToken);
    }
```

This drops the prior "tag not found" / "already 0" warning logs intentionally (they ran off the now-removed read; a decrement on a missing/zero tag is a benign no-op). Note this in the PR body. `using Microsoft.EntityFrameworkCore;` is already present (used by `IncrementUsageAsync`).

- [ ] **Step 4: Run both decrement tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TagDefinitionsRepositoryRaceTests"`
Expected: PASS, including running the concurrency test reliably (final count `== 0`). Run it 2-3 times to confirm stability.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs \
        TelegramGroupsAdmin.IntegrationTests/Repositories/TagDefinitionsRepositoryRaceTests.cs
git commit -F- <<'EOF'
fix(db): make DecrementUsageAsync atomic to close TOCTOU race

Replace read-check-decrement-save with a single guarded ExecuteUpdateAsync
(.Where(UsageCount > 0)). The WHERE clause preserves clamp-at-zero atomically,
eliminating lost updates under concurrent tag deletions. Drops the prior
not-found / already-zero warning logs (no longer have a prior read).

Closes #473
EOF
```

---

## Task 3: #495 — Two silent catch blocks swallow errors

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/Utilities/CheckResultsSerializer.cs` (~`:50-62`)
- Modify: `TelegramGroupsAdmin/Repositories/AnalyticsRepository.cs` (`ParseCheckResults`, ~`:250`)
- Modify: `TelegramGroupsAdmin.ContentDetection/ML/StopWordRecommendationService.cs` (~`:333`)
- Modify: `TelegramGroupsAdmin.ContentDetection/Services/VideoFrameExtractionService.cs` (`AnalyzeFrameBrightnessAsync`, ~`:628`)
- Test: `TelegramGroupsAdmin.UnitTests/ContentDetection/CheckResultsSerializerTests.cs` (2 malformed tests)

Background: the three `DetectionResultsRepository` callers already wrap `Deserialize` in `try/catch (Exception) { LogWarning; skip }`, but the serializer's silent catch defeats them. We make the serializer propagate, leave those three untouched, and add matching guards to the two unguarded callers.

- [ ] **Step 1: Flip the serializer's malformed-JSON tests to expect a throw (failing test)**

In `CheckResultsSerializerTests.cs`, replace the two tests in the "Malformed JSON" region:

```csharp
    [Test]
    public void Deserialize_MalformedJson_Throws()
    {
        // Malformed JSON must propagate, not be silently swallowed into an empty list.
        Assert.Throws<JsonException>(() => CheckResultsSerializer.Deserialize("{ this is not valid json !!!"));
    }

    [Test]
    public void Deserialize_ValidJsonButWrongShape_Throws()
    {
        // Valid JSON of the wrong shape (array instead of the CheckResults object) must propagate.
        Assert.Throws<JsonException>(() => CheckResultsSerializer.Deserialize("[1, 2, 3]"));
    }
```

(`using System.Text.Json;` is already present in this test file.) The null/empty/whitespace tests stay unchanged (they still return `[]`).

- [ ] **Step 2: Run the serializer tests to verify the two flipped tests fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~CheckResultsSerializerTests"`
Expected: the two malformed tests FAIL (current code returns `[]` instead of throwing).

- [ ] **Step 3: Remove the silent catch in CheckResultsSerializer**

In `CheckResultsSerializer.cs`, replace the `Deserialize` method's try/catch so exceptions propagate:

```csharp
    public static List<CheckResult> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var results = JsonSerializer.Deserialize<CheckResults>(json, DeserializeOptions);

        return results?.Checks ?? [];
    }
```

- [ ] **Step 4: Run the serializer tests to verify all pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~CheckResultsSerializerTests"`
Expected: PASS (malformed → throws; null/empty/whitespace → `[]`; valid → parsed).

- [ ] **Step 5: Add a fail-open guard to AnalyticsRepository.ParseCheckResults**

In `AnalyticsRepository.cs`, add `using System.Text.Json;` to the imports, then replace `ParseCheckResults`:

```csharp
    private List<CheckResult> ParseCheckResults(string? json)
    {
        try
        {
            return CheckResultsSerializer.Deserialize(json ?? string.Empty);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse check results JSON during analytics aggregation; treating as no checks");
            return [];
        }
    }
```

- [ ] **Step 6: Add a fail-open guard to StopWordRecommendationService**

In `StopWordRecommendationService.cs`, add `using System.Text.Json;` to the imports, then wrap the per-row `Deserialize` in the inner `foreach`:

```csharp
            foreach (var result in detectionResults)
            {
                List<CheckResult> checkResults;
                try
                {
                    checkResults = CheckResultsSerializer.Deserialize(result.CheckResultsJson!);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse check results JSON during stop word analysis; skipping malformed record");
                    continue;
                }

                var stopWordsCheck = checkResults.FirstOrDefault(c => c.CheckName == CheckName.StopWords);
```

(Leave the rest of the loop body unchanged.)

- [ ] **Step 7: Log before the brightness fallback in VideoFrameExtractionService**

In `VideoFrameExtractionService.cs`, change the bare `catch` at the end of `AnalyzeFrameBrightnessAsync`:

```csharp
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Frame brightness analysis failed for {FramePath}; defaulting to 128.0", framePath);
            return 128.0;
        }
```

(No new `using` needed — this catches general ffmpeg/process/IO failures, not `JsonException`.)

- [ ] **Step 8: Build and run the broader unit suite**

Run: `dotnet build`
Then: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ContentDetection"`
Expected: build succeeds; ContentDetection unit tests PASS.

- [ ] **Step 9: Commit**

```bash
git add TelegramGroupsAdmin.ContentDetection/Utilities/CheckResultsSerializer.cs \
        TelegramGroupsAdmin/Repositories/AnalyticsRepository.cs \
        TelegramGroupsAdmin.ContentDetection/ML/StopWordRecommendationService.cs \
        TelegramGroupsAdmin.ContentDetection/Services/VideoFrameExtractionService.cs \
        TelegramGroupsAdmin.UnitTests/ContentDetection/CheckResultsSerializerTests.cs
git commit -F- <<'EOF'
fix(detection): stop silently swallowing deserialization and brightness errors

CheckResultsSerializer.Deserialize now propagates JsonException instead of
returning an empty list, so corrupt CheckResultsJson is observable. The three
DetectionResultsRepository callers already catch+log+skip; add the same
fail-open guard to AnalyticsRepository and StopWordRecommendationService.
VideoFrameExtractionService logs before defaulting to 128.0 brightness.

Closes #495
EOF
```

---

## Task 4: #472 — QuartzSchedulingSyncService casts to concrete type for live re-sync

**Files:**
- Create: `TelegramGroupsAdmin.BackgroundJobs/Services/IScheduleResyncSignal.cs`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/QuartzSchedulingSyncService.cs`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/BackgroundJobConfigService.cs`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs`
- Test: `TelegramGroupsAdmin.UnitTests/BackgroundJobs/ScheduleResyncSignalTests.cs` (new)

Extract the wake-up semaphore (currently private inside the worker) into a shared singleton both services inject. This removes the concrete cast, the `_syncService` back-reference, and `SetSyncService`/`TriggerResync`.

- [ ] **Step 1: Write the failing signal unit test**

Create `TelegramGroupsAdmin.UnitTests/BackgroundJobs/ScheduleResyncSignalTests.cs` (mirror the namespace/style of an existing test in that project; the type under test lives in `TelegramGroupsAdmin.BackgroundJobs.Services`):

```csharp
using TelegramGroupsAdmin.BackgroundJobs.Services;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs;

[TestFixture]
public class ScheduleResyncSignalTests
{
    [Test]
    public async Task RequestResync_ThenWaitAsync_Completes()
    {
        var signal = new ScheduleResyncSignal();

        signal.RequestResync();

        await signal.WaitAsync(CancellationToken.None); // must complete without blocking
        Assert.Pass();
    }

    [Test]
    public async Task RequestResync_CalledTwiceBeforeWait_Coalesces()
    {
        var signal = new ScheduleResyncSignal();

        signal.RequestResync();
        signal.RequestResync(); // must not throw; collapses into one pending wake

        await signal.WaitAsync(CancellationToken.None); // first wait completes

        // A second wait should now block; assert it does NOT complete promptly.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await signal.WaitAsync(cts.Token));
    }
}
```

This references `ScheduleResyncSignal`, so the test project needs the type to be visible. If `ScheduleResyncSignal` is `internal`, add `[assembly: InternalsVisibleTo("TelegramGroupsAdmin.UnitTests")]` to the BackgroundJobs project (check whether an `InternalsVisibleTo` for the unit test project already exists there before adding a duplicate); otherwise make the class `public`. Prefer matching whatever the BackgroundJobs project already does for test visibility.

- [ ] **Step 2: Run the test to verify it fails to compile/run**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ScheduleResyncSignalTests"`
Expected: FAIL — `ScheduleResyncSignal` does not exist yet.

- [ ] **Step 3: Create the signal abstraction and implementation**

Create `TelegramGroupsAdmin.BackgroundJobs/Services/IScheduleResyncSignal.cs`:

```csharp
namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// A one-slot wake-up signal decoupling the config writer (producer) from the
/// schedule-sync worker (consumer). Replaces the former concrete back-reference
/// (SetSyncService cast). Registered as a singleton so both sides share one instance.
/// </summary>
public interface IScheduleResyncSignal
{
    /// <summary>
    /// Request a re-sync. Coalescing: multiple calls before the next WaitAsync
    /// collapse into a single pending wake-up.
    /// </summary>
    void RequestResync();

    /// <summary>Await the next re-sync request (or cancellation).</summary>
    Task WaitAsync(CancellationToken cancellationToken);
}

internal sealed class ScheduleResyncSignal : IScheduleResyncSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestResync()
    {
        // maxCount = 1: Release when already signaled throws SemaphoreFullException,
        // which means a resync is already pending — safe to ignore (coalescing).
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);

    public void Dispose() => _signal.Dispose();
}
```

(If Step 1 required `public` instead of `InternalsVisibleTo`, make `ScheduleResyncSignal` `public sealed`.)

- [ ] **Step 4: Run the signal test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ScheduleResyncSignalTests"`
Expected: PASS (both tests).

- [ ] **Step 5: Register the signal singleton**

In `ServiceCollectionExtensions.cs`, add the registration immediately before the synchronizer registration (~line 41, just before `AddSingleton<IQuartzScheduleSynchronizer, ...>`):

```csharp
        // Shared wake-up signal decoupling the config writer from the sync worker (replaces the concrete cast).
        services.AddSingleton<IScheduleResyncSignal, ScheduleResyncSignal>();
```

- [ ] **Step 6: Consume the signal in QuartzSchedulingSyncService**

In `QuartzSchedulingSyncService.cs`:

1. Add `IScheduleResyncSignal resyncSignal` to the primary constructor:
```csharp
public class QuartzSchedulingSyncService(
    ILogger<QuartzSchedulingSyncService> logger,
    ISchedulerFactory schedulerFactory,
    IBackgroundJobConfigService jobConfigService,
    IQuartzScheduleSynchronizer synchronizer,
    IScheduleResyncSignal resyncSignal) : BackgroundService
{
```
2. **Delete** the field `private readonly SemaphoreSlim _resyncSignal = new(0, 1);`.
3. **Delete** the entire registration block (the comment + `if (jobConfigService is BackgroundJobConfigService configService) { configService.SetSyncService(this); ... }`).
4. In the loop, change the wait from the deleted field to the injected signal:
```csharp
                    // Block until a re-sync is requested or cancellation is requested
                    await resyncSignal.WaitAsync(stoppingToken);
```
5. **Delete** the `public void TriggerResync()` method in its entirety.
6. **Delete** the `public override void Dispose()` override (it only disposed the removed semaphore; the base `BackgroundService.Dispose()` suffices and the signal singleton is disposed by the DI container).

- [ ] **Step 7: Raise the signal from BackgroundJobConfigService**

In `BackgroundJobConfigService.cs`:

1. **Delete** the field `private volatile QuartzSchedulingSyncService? _syncService;`.
2. **Delete** the `SetSyncService` method in its entirety.
3. Add a readonly field alongside the others:
```csharp
    private readonly IScheduleResyncSignal _resyncSignal;
```
4. Add the constructor parameter and assignment:
```csharp
    public BackgroundJobConfigService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<BackgroundJobConfigService> logger,
        IQuartzScheduleConverter scheduleConverter,
        IScheduleResyncSignal resyncSignal)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _scheduleConverter = scheduleConverter;
        _resyncSignal = resyncSignal;
    }
```
5. Replace the resync trigger (in `UpdateJobConfigAsync`):
```csharp
            _logger.LogDebug("Triggering Quartz re-sync for {JobName} due to config change", jobName);
            _resyncSignal.RequestResync();
```

- [ ] **Step 8: Build and fix any constructor call sites**

Run: `dotnet build`
Two constructors changed signature. Fix any compile errors:
- `QuartzSchedulingSyncService` is resolved by DI (`AddHostedService`) — no manual construction in production. Check tests: `grep -rn "new QuartzSchedulingSyncService(\|new BackgroundJobConfigService(" --include=*.cs`. For any test that manually constructs either, pass a signal: a real `new ScheduleResyncSignal()` (for the config service, where you want to observe a request) or `Substitute.For<IScheduleResyncSignal>()`.
Expected: build succeeds after fixes.

- [ ] **Step 9: Run the BackgroundJobs-related unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BackgroundJob"`
Then also: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~Quartz"`
Expected: PASS. (If a test asserted the old `TriggerResync`/`SetSyncService` wiring, update it to assert `_resyncSignal.Received(1).RequestResync()` via a substitute instead.)

- [ ] **Step 10: Commit**

```bash
git add TelegramGroupsAdmin.BackgroundJobs/Services/IScheduleResyncSignal.cs \
        TelegramGroupsAdmin.BackgroundJobs/Services/QuartzSchedulingSyncService.cs \
        TelegramGroupsAdmin.BackgroundJobs/Services/BackgroundJobConfigService.cs \
        TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs \
        TelegramGroupsAdmin.UnitTests/BackgroundJobs/ScheduleResyncSignalTests.cs
git commit -F- <<'EOF'
refactor(jobs): decouple live re-sync via a shared signal singleton

Replace the concrete-type cast (QuartzSchedulingSyncService casting
IBackgroundJobConfigService to BackgroundJobConfigService to call
SetSyncService) with a shared IScheduleResyncSignal both services inject.
Deletes the back-reference field, SetSyncService, and TriggerResync. The
DI cycle stays broken; a missing registration now fails fast at startup
instead of silently disabling re-sync.

Closes #472
EOF
```

---

## Task 5: Final verification and PR

- [ ] **Step 1: Full build**

Run: `dotnet build`
Expected: succeeds with no new warnings.

- [ ] **Step 2: Run the full unit suite**

Run: `dotnet test TelegramGroupsAdmin.UnitTests`
Expected: PASS.

- [ ] **Step 3: Run the affected integration tests (background, file output)**

Run (in background per convention):
`dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TagDefinitionsRepository"`
Expected: PASS, including the new decrement-race test run a few times.

- [ ] **Step 4: Confirm the commit-per-issue history**

Run: `git log --oneline develop..HEAD`
Expected: the spec doc commit plus one fix/refactor commit per issue (#475, #473, #495, #472).

- [ ] **Step 5: Push and open the PR to develop**

```bash
git push -u origin fix/correctness-bug-sweep
gh pr create --base develop --title "fix: correctness bug sweep (#495, #475, #473, #472)" --body "$(cat <<'EOF'
Closes #495
Closes #475
Closes #473
Closes #472

Four triaged correctness bugs, one commit each. Spec:
docs/superpowers/specs/2026-05-30-correctness-bug-sweep-design.md

- #475 audit: blacklist mutations pass target: null (was actor twice).
- #473 db: DecrementUsageAsync is now an atomic ExecuteUpdateAsync with a
  clamp-at-zero WHERE guard; drops the prior not-found/already-zero warnings.
- #495 detection: CheckResultsSerializer.Deserialize propagates JsonException;
  AnalyticsRepository and StopWordRecommendationService fail open per-row with
  a log; VideoFrameExtractionService logs before the 128.0 fallback.
- #472 jobs: shared IScheduleResyncSignal singleton replaces the concrete cast,
  back-reference field, SetSyncService, and TriggerResync.

#468 (/report Markdown parse) is intentionally excluded as a standalone PR.
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- #495 — serializer propagate (Task 3 S3), 3 repo callers unchanged (noted), 2 unguarded callers guarded (S5, S6), VideoFrame log (S7), tests flipped (S1). ✅
- #475 — 4 call sites → `target: null` (Task 1 S3), 5 positive assertions (S1). ✅
- #473 — atomic `ExecuteUpdateAsync` clamp (Task 2 S3), 20-concurrent + clamp tests (S1). ✅
- #472 — shared signal, deletes cast/field/SetSyncService/TriggerResync, DI fail-fast (Task 4). ✅

**Placeholder scan:** No TBD/TODO; every code step shows complete code. The only conditional ("if internal, add InternalsVisibleTo; else public") is a real, decidable branch with both outcomes specified.

**Type consistency:** `IScheduleResyncSignal` / `ScheduleResyncSignal` / `RequestResync` / `WaitAsync` consistent across Tasks 4 definitions, registration, and both consumers. `Arg.Is<Actor?>(t => t == null)` consistent across the 5 assertions. `DecrementUsageAsync` signature unchanged (callers unaffected).
