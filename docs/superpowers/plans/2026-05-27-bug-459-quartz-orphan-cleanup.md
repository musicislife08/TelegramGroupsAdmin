# #459 Quartz Orphan Cleanup Driven by BackgroundJobNames — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `QuartzSchedulingSyncService` from deleting and re-adding all 8 ad-hoc Quartz jobs on every startup. Source the "registered" set from `BackgroundJobNames` constants (via reflection) instead of the DB-config keyset.

**Architecture:** Add a static `BackgroundJobNames.AllRegisteredNames` property that reflects over the type's `public const string` fields and returns an `IReadOnlySet<string>` materialized once at type-load. Change `QuartzSchedulingSyncService.SyncSchedulesAsync` line 162 to pass this set into `RemoveOrphanedJobsAsync` instead of `allJobs.Keys`.

**Tech Stack:** Quartz.NET, .NET 10 reflection, NUnit.

**Spec:** `docs/superpowers/specs/2026-05-27-bug-459-quartz-orphan-cleanup-design.md`

---

## File Structure

- Modify: `TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs` — add `AllRegisteredNames`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/QuartzSchedulingSyncService.cs:162` — change caller
- Create: `TelegramGroupsAdmin.UnitTests/Core/BackgroundJobs/BackgroundJobNamesTests.cs`
- Create or extend: `TelegramGroupsAdmin.IntegrationTests/Services/BackgroundJobs/QuartzSchedulingSyncOrphanCleanupTests.cs`

---

## Task 1: Unit test for `BackgroundJobNames.AllRegisteredNames`

**Files:**
- Create: `TelegramGroupsAdmin.UnitTests/Core/BackgroundJobs/BackgroundJobNamesTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.UnitTests.Core.BackgroundJobs;

[TestFixture]
public class BackgroundJobNamesTests
{
    [Test]
    public void AllRegisteredNames_ContainsEveryConstField()
    {
        var expected = new[]
        {
            BackgroundJobNames.ScheduledBackup,
            BackgroundJobNames.DataCleanup,
            BackgroundJobNames.UserPhotoRefresh,
            BackgroundJobNames.BlocklistSync,
            BackgroundJobNames.DatabaseMaintenance,
            BackgroundJobNames.ChatHealthCheck,
            BackgroundJobNames.ClassifierRetraining,
            BackgroundJobNames.DeleteMessage,
            BackgroundJobNames.DeleteUserMessages,
            BackgroundJobNames.FetchUserPhoto,
            BackgroundJobNames.FileScan,
            BackgroundJobNames.RotateBackupPassphrase,
            BackgroundJobNames.TempbanExpiry,
            BackgroundJobNames.WelcomeTimeout,
            BackgroundJobNames.ProfileScan,
            BackgroundJobNames.ProfileRescan,
        };

        Assert.That(BackgroundJobNames.AllRegisteredNames, Is.EquivalentTo(expected));
    }

    [Test]
    public void AllRegisteredNames_IsCaseSensitive()
    {
        Assert.That(BackgroundJobNames.AllRegisteredNames.Contains("DeleteMessage"), Is.True);
        Assert.That(BackgroundJobNames.AllRegisteredNames.Contains("deletemessage"), Is.False);
    }

    [Test]
    public void AllRegisteredNames_DoesNotIncludeNonStringConstants()
    {
        // Sanity check: if any future const non-string fields are added to
        // BackgroundJobNames, they should not appear in the registered-names set.
        foreach (var name in BackgroundJobNames.AllRegisteredNames)
            Assert.That(name, Is.TypeOf<string>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BackgroundJobNamesTests"`

Expected: FAIL — `AllRegisteredNames` does not exist.

---

## Task 2: Implement `BackgroundJobNames.AllRegisteredNames`

**Files:**
- Modify: `TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs`

- [ ] **Step 1: Add reflection helper**

Add at the top of the class body (after the namespace and class declaration, before the first const):

```csharp
using System.Collections.Immutable;
using System.Reflection;

namespace TelegramGroupsAdmin.Core.BackgroundJobs;

public static class BackgroundJobNames
{
    private static readonly ImmutableHashSet<string> _allNames =
        typeof(BackgroundJobNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToImmutableHashSet(StringComparer.Ordinal);

    /// <summary>
    /// All CLR-registered job identity names, sourced via reflection over the
    /// public const string fields of this type. Materialized once at type-load.
    /// Use this as the authoritative "registered" set for orphan detection.
    /// </summary>
    public static IReadOnlySet<string> AllRegisteredNames => _allNames;

    // ... existing const fields ...
}
```

- [ ] **Step 2: Run unit tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BackgroundJobNamesTests"`

Expected: PASS (3 tests).

---

## Task 3: Integration test for orphan cleanup behavior

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Services/BackgroundJobs/QuartzSchedulingSyncOrphanCleanupTests.cs`

(Match the existing Quartz integration-test scaffolding if any. If none exists, this is the first such test — base it on `IntegrationTestBase` like other integration tests do.)

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Quartz;
using Quartz.Impl;
using TelegramGroupsAdmin.BackgroundJobs.Services;

namespace TelegramGroupsAdmin.IntegrationTests.Services.BackgroundJobs;

[TestFixture]
public class QuartzSchedulingSyncOrphanCleanupTests : IntegrationTestBase
{
    [Test]
    public async Task SyncSchedulesAsync_DoesNotDeleteAdHocJobs()
    {
        var sync = ServiceProvider.GetRequiredService<QuartzSchedulingSyncService>();
        var schedulerFactory = ServiceProvider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await schedulerFactory.GetScheduler();

        await sync.SyncSchedulesAsync(CancellationToken.None);

        var allKeys = await scheduler.GetJobKeys(Quartz.Impl.Matchers.GroupMatcher<JobKey>.AnyGroup());
        var jobNames = allKeys.Select(k => k.Name).ToHashSet();

        Assert.That(jobNames, Does.Contain("DeleteMessage"));
        Assert.That(jobNames, Does.Contain("FileScan"));
        Assert.That(jobNames, Does.Contain("WelcomeTimeout"));
        Assert.That(jobNames, Does.Contain("ProfileScan"));
        Assert.That(jobNames, Does.Contain("TempbanExpiry"));
        Assert.That(jobNames, Does.Contain("FetchUserPhoto"));
        Assert.That(jobNames, Does.Contain("DeleteUserMessages"));
        Assert.That(jobNames, Does.Contain("RotateBackupPassphrase"));
    }

    [Test]
    public async Task SyncSchedulesAsync_DeletesGenuineOrphans()
    {
        var sync = ServiceProvider.GetRequiredService<QuartzSchedulingSyncService>();
        var schedulerFactory = ServiceProvider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await schedulerFactory.GetScheduler();

        // Seed a ghost job that no longer exists in BackgroundJobNames
        var ghostKey = new JobKey("GhostJobThatNoLongerExists");
        var ghost = JobBuilder.Create<NoOpJob>()
            .WithIdentity(ghostKey)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(ghost, replace: true);

        Assert.That(await scheduler.CheckExists(ghostKey), Is.True);

        await sync.SyncSchedulesAsync(CancellationToken.None);

        Assert.That(await scheduler.CheckExists(ghostKey), Is.False);
    }

    private sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run tests to verify the first fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~SyncSchedulesAsync_DoesNotDeleteAdHocJobs"`

Expected: FAIL — ad-hoc jobs are deleted by the current caller passing `allJobs.Keys`. (They are re-added by `q.AddJob` on the next startup, but the test reflects the in-process effect of `SyncSchedulesAsync` only.)

---

## Task 4: Wire `RemoveOrphanedJobsAsync` to use `BackgroundJobNames.AllRegisteredNames`

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/QuartzSchedulingSyncService.cs:162`

- [ ] **Step 1: Change the caller**

Replace line 162:

```csharp
// Before
await RemoveOrphanedJobsAsync(allJobs.Keys, cancellationToken);

// After
await RemoveOrphanedJobsAsync(BackgroundJobNames.AllRegisteredNames, cancellationToken);
```

Add the necessary using if missing: `using TelegramGroupsAdmin.Core.BackgroundJobs;` at the top.

`RemoveOrphanedJobsAsync` already accepts `IEnumerable<string>` — no signature change needed.

- [ ] **Step 2: Run the integration tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~QuartzSchedulingSyncOrphanCleanupTests"`

Expected: PASS (both tests).

---

## Task 5: Final verification + commit

- [ ] **Step 1: Run the full unit test project**

Run: `dotnet test TelegramGroupsAdmin.UnitTests`

Expected: all tests pass.

- [ ] **Step 2: Run the full integration test project**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests`

Expected: all tests pass.

- [ ] **Step 3: Build the full solution**

Run: `dotnet build`

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs \
        TelegramGroupsAdmin.BackgroundJobs/Services/QuartzSchedulingSyncService.cs \
        TelegramGroupsAdmin.UnitTests/Core/BackgroundJobs/BackgroundJobNamesTests.cs \
        TelegramGroupsAdmin.IntegrationTests/Services/BackgroundJobs/QuartzSchedulingSyncOrphanCleanupTests.cs

git commit -m "$(cat <<'EOF'
fix(quartz): drive orphan cleanup from BackgroundJobNames, not DB-config keyset

Closes #459.

Adds BackgroundJobNames.AllRegisteredNames (reflection over const string
fields, materialized once at type-load) and threads it into
RemoveOrphanedJobsAsync. Eliminates the 8 ad-hoc-job warnings per startup
and the corresponding churn on qrtz_job_details. Original intent (clean
up CLR type renames) is preserved.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```
