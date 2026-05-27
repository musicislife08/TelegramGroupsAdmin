# #459 — Quartz orphan-cleanup misclassifies ad-hoc jobs

Closes #459

## Problem

`QuartzSchedulingSyncService.RemoveOrphanedJobsAsync` deletes every ad-hoc Quartz `JobDetail` on every startup. The 8 ad-hoc jobs (`DeleteMessage`, `DeleteUserMessages`, `FetchUserPhoto`, `FileScan`, `RotateBackupPassphrase`, `TempbanExpiry`, `WelcomeTimeout`, `ProfileScan`) are CLR-registered as durable via `q.AddJob<T>().StoreDurably()` but have no entry in `configs.background_jobs_config` because they aren't user-schedulable.

The orphan detector receives `allJobs.Keys` from `SyncSchedulesAsync` (line 162), which is the DB-config keyset — so ad-hoc jobs always look orphaned. They are re-added on the next `q.AddJob` pass, leaving the symptom as 8 warning log lines per startup plus needless churn of `qrtz_job_details` rows. One latent footgun: if the manual-trigger UI is ever extended to ad-hoc jobs, `JobTriggerService.TriggerNowAsync` (which gates on `scheduler.CheckExists`) would fail in the gap between delete and re-add.

Full diagnosis: [issue #459](https://github.com/musicislife08/issues/459).

## Approach: feed the CLR-registered set into the orphan detector

`BackgroundJobNames` is already the canonical name registry (15 `public const string` fields). Pull its contents via reflection and pass that to `RemoveOrphanedJobsAsync` instead of the DB-config keyset.

Rejected alternatives:
- Manual `AllJobNames` array on `BackgroundJobNames` — has the same drift failure mode the bug itself demonstrates (silent divergence between two name registries).
- `TypeLoadException`-only orphan detection (the issue's Approach 2) — more code, harder to test, and doesn't add value beyond the simpler name-set comparison.
- Querying the live scheduler — at the orphan-detection point, the scheduler contains both freshly-registered jobs and stored orphans from prior runs; it can't distinguish them without an external source of truth.

## Implementation

### 1. Add reflection helper on `BackgroundJobNames`

`TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs`:

```csharp
private static readonly ImmutableHashSet<string> _allNames =
    typeof(BackgroundJobNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToImmutableHashSet(StringComparer.Ordinal);

public static IReadOnlySet<string> AllRegisteredNames => _allNames;
```

- Uses `GetRawConstantValue()` (not `GetValue(null)`) — the correct API for compile-time `const` fields.
- `ImmutableHashSet<string>` materialized once at type-load (effectively startup); no per-call cost.
- `StringComparer.Ordinal` matches Quartz's case-sensitive name comparisons.

### 2. Change the caller in `QuartzSchedulingSyncService.SyncSchedulesAsync`

Line 162 currently:

```csharp
await RemoveOrphanedJobsAsync(allJobs.Keys, cancellationToken);
```

Changes to:

```csharp
await RemoveOrphanedJobsAsync(BackgroundJobNames.AllRegisteredNames, cancellationToken);
```

`RemoveOrphanedJobsAsync` already accepts `IEnumerable<string>` and builds a hashset internally — no signature change needed.

### 3. Update log message

The warning at `QuartzSchedulingSyncService.cs:183` reads `"type no longer registered"` but the predicate previously tested DB-config presence, not CLR-type presence. Now that the predicate matches the message, leave the wording at `Warning` level — a real orphan after a CLR rename is genuinely worth flagging.

## Files

- `TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs` — add reflection helper.
- `TelegramGroupsAdmin.BackgroundJobs/Services/QuartzSchedulingSyncService.cs:162` — change caller.

## Tests

Unit test on `BackgroundJobNames.AllRegisteredNames`:

- Returns exactly 16 names (8 scheduled + 8 ad-hoc as of this writing). Update the count assertion when new jobs are added — failing the test is the intended signal to also wire the new job into `RegisterJobs`.
- Includes every public const string field declared on the type.
- Comparer is case-sensitive (`Contains("DeleteMessage")` true, `Contains("deletemessage")` false).

Integration test on the orphan-cleanup behavior (uses the existing Quartz integration test infrastructure if any; otherwise a focused new test):

- Seed a `JobDetail` with key `"GhostJobThatNoLongerExists"` (a name not in `BackgroundJobNames`).
- Run `SyncSchedulesAsync`.
- Assert the ghost job is deleted.
- Assert all ad-hoc jobs (`DeleteMessage`, `FileScan`, etc.) are still present after sync.
- Assert no warning log lines for ad-hoc jobs.

## Acceptance Criteria

- [ ] `BackgroundJobNames.AllRegisteredNames` returns all const string fields via reflection, materialized once.
- [ ] `SyncSchedulesAsync` passes `BackgroundJobNames.AllRegisteredNames` to `RemoveOrphanedJobsAsync` instead of `allJobs.Keys`.
- [ ] Cold startup against an empty Quartz store produces zero `"Removed orphaned Quartz job"` warnings.
- [ ] Cold startup against a seeded ghost-job entry produces exactly one warning (for that ghost) and deletes it.
- [ ] All 8 ad-hoc jobs remain present in `qrtz_job_details` after `SyncSchedulesAsync` completes.

## Out of Scope

- #461 (background-job identity naming standardization) — orthogonal, will be easier after this fix lands.
- Refactoring `RegisterJobs` to drive itself from `BackgroundJobNames` (single source of truth for both registration and orphan detection) — interesting but larger, and Quartz's `IServiceCollectionQuartzConfigurator` shape makes the `AddJob<T>` calls hard to generate from string constants without losing the generic type bind.
- Exposing the manual-trigger UI for ad-hoc jobs — that's the latent footgun this fix protects against, but the UI change is a separate piece of work.
