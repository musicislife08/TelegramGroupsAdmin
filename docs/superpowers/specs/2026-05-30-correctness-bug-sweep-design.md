# Correctness bug sweep — #495, #475, #473, #472

Closes #495
Closes #475
Closes #473
Closes #472

One branch (`fix/correctness-bug-sweep`) → one PR to `develop`, with a commit per issue plus doc commits.
#468 (`/report` Markdown parse) is deliberately excluded and tracked as a standalone PR per the
ParseMode-churn-standalone convention.

---

## #495 — Two silent catch blocks swallow errors

### Problem

Two `catch` blocks return fallback values with no logging, so the failure path is invisible.

1. `CheckResultsSerializer.Deserialize` (`TelegramGroupsAdmin.ContentDetection/Utilities/CheckResultsSerializer.cs:57`)
   has `catch { return []; }`. A deserialization failure returns an empty `List<CheckResult>`, so
   callers see "no checks ran" instead of "the stored data was corrupt." This already bit us once
   (migration `20260307182307_FixCheckResultsJsonCheckNameType` exists because malformed `CheckName`
   enum data was being silently swallowed here).
2. `VideoFrameExtractionService.AnalyzeFrameBrightnessAsync`
   (`TelegramGroupsAdmin.ContentDetection/Services/VideoFrameExtractionService.cs:~628`) has
   `catch { return 128.0; }`. Any ffmpeg/process/IO failure returns a hardcoded "normal brightness"
   with no trace, silently degrading black-frame detection.

### Key finding — the callers already expect a throw

The three `DetectionResultsRepository` call sites (`:503`, `:549`, `:604`) **already** wrap
`Deserialize` in `try { … } catch (Exception ex) { _logger.LogWarning(…); /* skip - fail open */ }`.
They were written expecting `Deserialize` to throw on corrupt JSON — but the serializer's internal
silent catch means those handlers **can never fire**. A corrupt row silently becomes "zero checks"
instead of "skip the row and log it."

Two other callers do NOT wrap it:
- `AnalyticsRepository.ParseCheckResults` (`TelegramGroupsAdmin/Repositories/AnalyticsRepository.cs:252`)
- `StopWordRecommendationService` (`TelegramGroupsAdmin.ContentDetection/ML/StopWordRecommendationService.cs:333`, inside a `foreach`)

So the correct policy is: **the serializer should not decide the fallback** — it should propagate, and
each caller fails open per-row with a log. This makes every corrupt row observable and lets the
existing `DetectionResultsRepository` handlers do their job.

### Approach

**`CheckResultsSerializer.Deserialize`** — **remove the `try/catch` entirely** so any exception
(`JsonException` and anything else unexpected) propagates to the caller. The empty/whitespace
short-circuit (`return []`) and the `results?.Checks ?? []` null-coalesce both stay. The static class
needs no logger because logging moves to the callers, which all have one.

**Callers:**
- `DetectionResultsRepository` (`:503`, `:549`, `:604`) — **no change**. Their existing
  `catch (Exception ex) { _logger.LogWarning(…) }` now actually fires and skips the row.
- `AnalyticsRepository.ParseCheckResults` — wrap the `Deserialize` call in a per-row
  `try/catch (JsonException ex) { _logger.LogWarning(ex, …); return []; }`, matching the
  fail-open pattern from `DetectionResultsRepository`.
- `StopWordRecommendationService` (`:333`) — wrap the per-row `Deserialize` inside the `foreach`
  with the same guard (`LogWarning` + `continue`).

**`VideoFrameExtractionService.AnalyzeFrameBrightnessAsync`** — change `catch` to
`catch (Exception ex)` and add
`_logger.LogWarning(ex, "Frame brightness analysis failed for {FramePath}; defaulting to 128.0", framePath)`
before `return 128.0;`. `_logger` is already injected and used throughout. The fallback is genuinely
fine here (one frame degrading gracefully); we only need observability.

### Tests

`TelegramGroupsAdmin.UnitTests/ContentDetection/CheckResultsSerializerTests.cs`:
- The two malformed-input tests that currently assert `Deserialize` returns `[]`
  (`:55` `"{ this is not valid json !!!"`, `:65` `"[1, 2, 3]"`) flip to assert it throws `JsonException`.
- The null/empty/whitespace tests (`:21`, `:31`, `:41`) stay green (still return `[]`).
- All valid-JSON tests are unaffected.

Add/confirm caller coverage where it already exists; a unit test asserting `AnalyticsRepository` /
`StopWordRecommendationService` skip a malformed row without throwing is welcome if cheap, but the
primary contract change is exercised by the serializer tests.

### Acceptance criteria

- [ ] Neither catch block is silent.
- [ ] `Deserialize` propagates `JsonException` (bare catch removed / narrowed-and-rethrown).
- [ ] `AnalyticsRepository` and `StopWordRecommendationService` fail open per-row with a `LogWarning`.
- [ ] `VideoFrameExtractionService` logs before the `128.0` fallback.
- [ ] `CheckResultsSerializerTests` updated: malformed JSON now asserts a throw; null/empty still `[]`.

---

## #475 — UsernameBlacklistService passes actor as both actor and target

### Problem

The four mutation methods on `UsernameBlacklistService` (`AddEntryAsync`, `DeleteEntryAsync`,
`SetEnabledAsync`, `UpdateNotesAsync`) call
`auditService.LogEventAsync(eventType, actor, actor, pattern, ct)` — binding the admin `actor` into
the `target` slot. Blacklist mutations operate on configuration, not a discrete user, so `target`
should be `null`. The duplicate actor produces misleading audit records and pollutes target-based
filtering (`GetEventsForUserAsync` / `targetUserIdFilter`).

`IAuditService.LogEventAsync` (`TelegramGroupsAdmin.Core/Services/IAuditService.cs:10`) already
defaults `target` to `null`. The convention for config-style events is to omit it
(`RotateBackupPassphraseJob.cs:182` uses `target: null`; `TelegramSessionManager` / `TelegramAuthService`
omit it).

### Approach

In `TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs`, change all four
`LogEventAsync` calls (`:60-65`, `:75-77`, `:90`, `:100-104`) to pass `target: null` (named, for
clarity) instead of the second positional `actor`.

### Tests

`TelegramGroupsAdmin.UnitTests/Telegram/Services/UsernameBlacklistServiceMutationTests.cs`:
- The four positive-path `Received(1)` assertions that currently expect `actor, actor` change to
  expect a `null` target (e.g. `Arg.Is<Actor?>(t => t == null)`).
- The two `DidNotReceive` tests use `Arg.Any<Actor?>()` and need no change.

### Acceptance criteria

- [ ] All four `LogEventAsync` calls pass `null` for `target`.
- [ ] The four positive-path assertions verify a `null` target.
- [ ] `DidNotReceive` tests unchanged and green.
- [ ] No other audit call sites touched.

---

## #473 — TagDefinitionsRepository.DecrementUsageAsync TOCTOU race

### Problem

`TagDefinitionsRepository.DecrementUsageAsync` (`:131-154`) reads the row, checks `UsageCount > 0` in
memory, decrements the tracked entity, then `SaveChangesAsync`. Read and write are separate
round-trips with no DB guard, so concurrent decrements lose updates (two callers read N, both write
N-1). The sibling `IncrementUsageAsync` (`:116-129`) is already atomic. Sole caller is
`UserTagsRepository.DeleteTagAsync` (`:74`); concurrent deletes of distinct `user_tags` rows sharing a
tag name race here. This was the item explicitly deferred by the #407 sweep (PR #479).

### Approach — atomic `ExecuteUpdateAsync` with the clamp in the WHERE clause

```csharp
await context.TagDefinitions
    .Where(t => t.TagName == normalizedTag && t.UsageCount > 0)
    .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsageCount, t => t.UsageCount - 1), cancellationToken);
```

- Single atomic UPDATE; the `UsageCount > 0` filter makes the below-zero case a no-op, preserving
  today's clamp-at-zero behavior. Drops the `FirstOrDefaultAsync` read.
- **`ExecuteUpdateAsync` (LINQ), not raw SQL** — deliberately diverges from the #407 sweep's
  `ExecuteSqlAsync(... ON CONFLICT ...)` because it keeps us on the Fluent/LINQ path
  (`prefer-EF-Core-over-raw-SQL`) while still compiling to one atomic statement. `ExecuteUpdateAsync`
  is already used elsewhere (`BanCelebrationGifRepository`, `TelegramUserRepository`).
- **Logging change:** the existing "tag not found" / "already 0" warning logs are dropped — they ran
  off the prior read, which no longer happens. These were low-value (a decrement on a missing/zero tag
  is a benign no-op). Intentional; called out in the PR body.

### Tests

`TelegramGroupsAdmin.IntegrationTests/Repositories/TagDefinitionsRepositoryRaceTests.cs` — extend with
a concurrent-decrement test mirroring the existing
`IncrementUsageAsync_ConcurrentCalls_FinalCountEqualsCallCount` harness:
1. Seed a tag with `usage_count = N` (e.g. 20).
2. Fire 20 concurrent `DecrementUsageAsync` calls, each from its own DI scope / `DbContext`.
3. Assert final `usage_count == 0` (i.e. `N - 20`, clamped).

A second small test: decrementing a tag already at 0 leaves it at 0 (no negative).

### Acceptance criteria

- [ ] `DecrementUsageAsync` is a single atomic `ExecuteUpdateAsync` (no read-then-write).
- [ ] Clamp-at-zero preserved; never goes negative.
- [ ] 20-concurrent-decrement integration test asserts final count `== N - decrements` (clamped).
- [ ] Existing `TagDefinitionsRepository` integration tests pass; dropped warning logs noted in PR.

---

## #472 — QuartzSchedulingSyncService casts to concrete type for live re-sync

### Problem

`QuartzSchedulingSyncService` registers its resync callback by downcasting the injected
`IBackgroundJobConfigService` to the concrete `BackgroundJobConfigService`
(`QuartzSchedulingSyncService.cs:42-46`), calling `SetSyncService(this)`. `SetSyncService` is not on the
interface. The config service stores the back-reference in
`volatile QuartzSchedulingSyncService? _syncService` and calls `_syncService?.TriggerResync()` on
schedule/enabled changes (`BackgroundJobConfigService.cs:22`, `:50-53`, `:158-162`). If the resolved
implementation is ever not exactly `BackgroundJobConfigService` (test double, decorator), the `is`
match fails silently, `_syncService` stays null, and live re-sync stops with no error — until the next
restart.

### What we're actually solving

Two long-lived singletons with an asymmetric relationship:
- **Worker** `QuartzSchedulingSyncService` has a real data dependency on the config service (reads
  config) → legitimate constructor injection.
- **Config writer** `BackgroundJobConfigService` only needs to deliver a one-way "config changed —
  re-sync now" **wake-up signal**.

A constructor edge for that signal would create a DI cycle (A→B, B→A). The entire `SetSyncService` /
cast / lazy-null-field / silent-`?.` apparatus exists only to smuggle that one signal across the cycle.

### Approach — extract the wake-up signal into a shared singleton (no events)

The synchronization primitive already exists *inside* the worker:
`SemaphoreSlim _resyncSignal = new(0, 1)`; `TriggerResync()` does a guarded `Release()` (coalescing
rapid edits via `catch (SemaphoreFullException)`); the loop does `_resyncSignal.WaitAsync()`. Lift it
out into its own injectable object that both services depend on.

```csharp
public interface IScheduleResyncSignal
{
    void RequestResync();                     // producer — the config writer
    Task WaitAsync(CancellationToken cancellationToken);  // consumer — the worker loop
}

internal sealed class ScheduleResyncSignal : IScheduleResyncSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestResync()
    {
        try { _signal.Release(); }            // same coalescing semantics as today's TriggerResync
        catch (SemaphoreFullException) { }    // a resync is already pending
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);
}
```

Events were considered and rejected: handler subscriptions rooted on long-lived singletons are a
classic managed-memory leak and add invocation-ordering / thread-affinity concerns. A shared signal
object has nothing to unsubscribe.

**Wiring changes:**
- DI: `services.AddSingleton<IScheduleResyncSignal, ScheduleResyncSignal>();` in
  `TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs` (near `:42-45`).
- `BackgroundJobConfigService` — inject `IScheduleResyncSignal`; replace `_syncService?.TriggerResync()`
  (`:161`) with `_resyncSignal.RequestResync()`. **Delete** the `_syncService` field (`:22`) and
  `SetSyncService` (`:50-53`).
- `QuartzSchedulingSyncService` — inject `IScheduleResyncSignal`; the loop awaits
  `_resyncSignal.WaitAsync(stoppingToken)`. **Delete** the private `SemaphoreSlim`, the
  `is BackgroundJobConfigService configService` block (`:42-46`), and the `TriggerResync()` method.

### Why this satisfies the issue's ACs

- No concrete cast, no abstraction leak.
- Resync works purely through abstractions; swapping the implementation can't silently break it.
- **Fail-fast, not silent:** if the signal isn't registered, DI throws at startup — the old silent
  `?.` failure mode is structurally impossible.
- DI cycle stays broken: both services depend on `IScheduleResyncSignal`; the signal depends on
  nothing; the writer no longer references the worker at all (strictly fewer couplings than today).

### Tests

- A focused unit test for `ScheduleResyncSignal`: `RequestResync` then `WaitAsync` completes;
  duplicate `RequestResync` before a wait coalesces (no throw, one wake).
- Confirm existing background-job config / sync tests still pass with the new wiring. Any test that
  reached `SetSyncService` or `TriggerResync` directly is updated to drive the signal instead.

### Acceptance criteria

- [ ] `QuartzSchedulingSyncService` no longer casts/`is`-checks `IBackgroundJobConfigService`.
- [ ] Resync wiring is through abstractions only (the shared signal); swapping the implementation does
      not silently disable live re-sync.
- [ ] A missing signal registration is observable (DI fail-fast at startup), not a silent null path.
- [ ] Live config-change re-sync still works end-to-end (schedule/enabled edit triggers a Quartz re-sync).
- [ ] DI circular dependency remains broken (no startup resolution cycle).

---

## Out of scope

- #468 (`/report` Markdown parse) — standalone PR per the ParseMode-churn-standalone convention.
- Any other `ParseMode.Markdown` call sites — covered by #468, not here.
- Repository methods with read-then-write shapes other than `DecrementUsageAsync` — fix as found in
  their own issues.
- Changing correct callers (`UserTagsRepository.DeleteTagAsync`, etc.) — only internals change.
