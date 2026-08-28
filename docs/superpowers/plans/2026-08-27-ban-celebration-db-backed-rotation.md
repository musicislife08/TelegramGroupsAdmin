# Database-Backed Ban Celebration Rotation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move ban celebration rotation state out of the in-memory shuffle bag and into a `dispensed_at` column on the two content tables, so a newly added GIF or caption is in the rotation at the instant its row commits.

**Architecture:** `NULL` in `dispensed_at` means "not yet dispensed this cycle" — i.e. in the bag. Claiming is one statement: an `UPDATE ... RETURNING id` that picks a random undispensed row with `FOR UPDATE SKIP LOCKED` and stamps it, run through EF's raw SQL APIs and materialized without composition. On exhaustion, a transaction-scoped advisory lock serializes the reset, a double-checked re-claim means the loser of a race never resets at all, and the reset holds back the last-dispensed row so it cannot open the next cycle. `BanCelebrationCache` and its interface are deleted entirely.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, PostgreSQL 18, NUnit, NSubstitute 6, Testcontainers-backed Postgres for integration tests.

**Spec:** `docs/superpowers/specs/2026-08-27-ban-celebration-db-backed-rotation-design.md`

## Global Constraints

- Branch is already created and checked out: `feat/ban-celebration-db-backed-rotation`, based on `develop`. Never commit to `master` or `develop`.
- Conventional commit prefixes (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`).
- EF Core workflow: modify models and `AppDbContext` FIRST, then `dotnet ef migrations add`. Never hand-write a migration file.
- Apply migrations with `dotnet run --migrate-only` from the app project. Never apply by hand-running SQL.
- **Stay inside EF's raw SQL APIs.** `SqlQueryRaw<int>(sql).ToListAsync(ct)` sends SQL verbatim and returns the `RETURNING` id; `ExecuteSqlRawAsync` runs the lock and reset statements. Do NOT drop to a raw `DbCommand` off `GetDbConnection()` — it works, but it bypasses EF's command interception and logging (which feed Seq and OpenTelemetry here) and leaves the execution strategy. That was tried and rejected.
- **Never apply a LINQ operator to a `SqlQueryRaw` that contains an `UPDATE`.** Materializing with `ToListAsync` is not composition; `FirstOrDefaultAsync`, `Where` or `Take` are — they wrap the SQL in a subquery, and Postgres rejects an `UPDATE` there, CTE-wrapped or not. Verified; see the spec's spike findings.
- No `AS "Value"` alias is needed on these scalar queries — verified positively, not merely by absence of an error.
- `pg_advisory_xact_lock` is scoped to its transaction. Every statement in a claim — lock, claim, reset, release — must run through the SAME `AppDbContext` inside one `BeginTransactionAsync`, or the lock protects nothing.
- Wrap the whole claim in `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`. Production configures `EnableRetryOnFailure`; an explicit transaction does not throw under it (verified against a retry-configured context, which the test fixtures are not), but the wrapper is what makes a transient failure retry the claim instead of surfacing as a skipped celebration.
- The only values interpolated into SQL are the two table-name constants inside `RotationCycleClaim`'s own switch. Nothing reaches it from a caller, and every runtime value binds as a `{0}` parameter.
- `TreatWarningsAsErrors` is on in `TelegramGroupsAdmin.Telegram`, and a switch expression over an enum with no default arm fails the build with CS8524 (verified). The switch therefore needs its `_ => throw` arm, which means a future third rotation added without a switch arm throws at runtime rather than breaking the build — its own tests are what catch it.
- No UI changes. `BanCelebrationSettings.razor` and the add dialogs are untouched.
- `GetRandomAsync` stays on both repositories — the settings-page preview uses it and deliberately does not participate in the rotation.
- Solution file: `TelegramGroupsAdmin.sln`. Integration tests need Docker running.

## File Structure

| File | Responsibility |
|---|---|
| `TelegramGroupsAdmin.Data/Models/BanCelebrationGifDto.cs` | + `DispensedAt` column |
| `TelegramGroupsAdmin.Data/Models/BanCelebrationCaptionDto.cs` | + `DispensedAt` column |
| `TelegramGroupsAdmin.Data/Constants/AdvisoryLockKeys.cs` | **new** — registry of advisory lock keys |
| `TelegramGroupsAdmin.Telegram/Repositories/RotationCycleClaim.cs` | **new** — the `RotationBag` enum and the claim/reset algorithm, shared by both repositories |
| `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationGifRepository.cs` | + `ClaimNextForCycleAsync`, − `GetAllIdsAsync` |
| `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationCaptionRepository.cs` | + `ClaimNextForCycleAsync`, − `GetAllIdsAsync` |
| `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs` | selection collapses to one repository call per content type |
| `TelegramGroupsAdmin.Telegram/Services/IBanCelebrationCache.cs`, `BanCelebrationCache.cs` | **deleted** |

The claim algorithm lives in its own file rather than being written twice: it is ~60 lines of statement orchestration, and duplicating it verbatim across two repositories is the kind of copy a reviewer should reject.

---

### Task 1: Schema and migration

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/BanCelebrationGifDto.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/BanCelebrationCaptionDto.cs`
- Create: `TelegramGroupsAdmin.Data/Migrations/<timestamp>_AddBanCelebrationDispensedAt.cs` (generated, never hand-written)

**Interfaces:**
- Consumes: nothing.
- Produces: `DateTimeOffset? BanCelebrationGifDto.DispensedAt` and `DateTimeOffset? BanCelebrationCaptionDto.DispensedAt`, mapped to a nullable `dispensed_at` column on each table. Tasks 2 and 3 read and write that column through raw SQL; Task 4 relies on the column existing.

- [ ] **Step 1: Add the property to both DTOs**

Append to `BanCelebrationGifDto` after `CreatedAt`, and the identical block to `BanCelebrationCaptionDto` after its `CreatedAt`:

```csharp
    /// <summary>
    /// When this item was dispensed in the CURRENT rotation cycle, or null if it is still pending.
    /// Cleared for every row when the cycle is exhausted — this is cycle state, not a durable
    /// "last shown" record.
    /// </summary>
    [Column("dispensed_at")]
    public DateTimeOffset? DispensedAt { get; set; }
```

- [ ] **Step 2: Confirm no `AppDbContext` change is needed**

Read the ban celebration section of `TelegramGroupsAdmin.Data/AppDbContext.cs` (search for `BanCelebrationGifs indexes`, around line 803). The column maps by attribute and the spec adds no index — at ~100 rows a partial index costs more to maintain than it saves. If nothing needs adding, add nothing; do not invent configuration to have something to write.

- [ ] **Step 3: Generate the migration**

Run:
```bash
dotnet ef migrations add AddBanCelebrationDispensedAt \
  --project TelegramGroupsAdmin.Data \
  --startup-project TelegramGroupsAdmin
```

- [ ] **Step 4: Read the generated migration and verify it**

Open the generated file. It must contain exactly two `AddColumn<DateTimeOffset>` calls — one per table — each with `nullable: true` and no default value. Existing rows (including the canonical test dataset's 92 GIFs and 74 captions) then take `NULL`, which means "pending", the correct starting state.

If it contains anything else — a dropped column, a renamed index, an unrelated table — stop and report it. That means the model snapshot was out of sync before you started, which is not yours to fix silently.

- [ ] **Step 5: Apply the migration**

Run:
```bash
dotnet run --project TelegramGroupsAdmin --migrate-only
```
Expected: migration applies and the process exits cleanly.

- [ ] **Step 6: Verify the build and that existing tests still pass**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebration`
Expected: 0 warnings, 0 errors; all tests pass. Integration tests build their schema from migrations, so a broken migration surfaces here.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Data/Models/BanCelebrationGifDto.cs \
        TelegramGroupsAdmin.Data/Models/BanCelebrationCaptionDto.cs \
        TelegramGroupsAdmin.Data/Migrations/
git commit -m "feat(celebration): add dispensed_at rotation column"
```

---

### Task 2: The claim algorithm and the GIF repository

**Files:**
- Create: `TelegramGroupsAdmin.Data/Constants/AdvisoryLockKeys.cs`
- Create: `TelegramGroupsAdmin.Telegram/Repositories/RotationCycleClaim.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IBanCelebrationGifRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationGifRepository.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationGifRepositoryTests.cs`

**Interfaces:**
- Consumes: `DispensedAt` / `dispensed_at` from Task 1.
- Produces:
  - `AdvisoryLockKeys.BanCelebrationGifCycle` (`long`, value `7_201_001`) and `AdvisoryLockKeys.BanCelebrationCaptionCycle` (`long`, value `7_201_002`), namespace `TelegramGroupsAdmin.Data.Constants`.
  - `internal enum RotationBag { BanCelebrationGifs, BanCelebrationCaptions }` and `internal static Task<int?> RotationCycleClaim.ClaimNextIdAsync(AppDbContext context, RotationBag bag, CancellationToken ct)`, both in namespace `TelegramGroupsAdmin.Telegram.Repositories` — Task 3 calls this with `RotationBag.BanCelebrationCaptions`.
  - `Task<BanCelebrationGif?> IBanCelebrationGifRepository.ClaimNextForCycleAsync(CancellationToken ct = default)` — Task 4 calls this.

- [ ] **Step 1: Read the settled SQL facts before writing any of it**

The spec's "Settled by spike" list is the result of throwaway integration tests already run against
Postgres 18 and EF Core 10, including a context configured with `EnableRetryOnFailure` as
production is. It settles: no CTE (a data-modifying CTE cannot be nested at all), `SqlQueryRaw` +
`ToListAsync` returns the `RETURNING` id, no LINQ operator may ever be applied to it, no `"Value"`
alias is needed, an explicit transaction does not throw under the retry strategy, and
`FOR UPDATE SKIP LOCKED` genuinely separates concurrent claims. Read that list. Do not reintroduce
a CTE, and do not drop to a raw `DbCommand` — both were tried and rejected with evidence.

- [ ] **Step 2: Write the advisory lock key registry**

Create `TelegramGroupsAdmin.Data/Constants/AdvisoryLockKeys.cs`:

```csharp
namespace TelegramGroupsAdmin.Data.Constants;

/// <summary>
/// Keys for PostgreSQL advisory locks. The key space is global to the database, so every
/// advisory lock in the application must take its key from this file — that is what makes a
/// collision between two unrelated features visible in one place.
/// </summary>
public static class AdvisoryLockKeys
{
    /// <summary>Serializes ban celebration GIF rotation-cycle exhaustion.</summary>
    public const long BanCelebrationGifCycle = 7_201_001;

    /// <summary>Serializes ban celebration caption rotation-cycle exhaustion.</summary>
    public const long BanCelebrationCaptionCycle = 7_201_002;
}
```

- [ ] **Step 3: Write the failing tests**

Add to `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationGifRepositoryTests.cs`, in a new `#region ClaimNextForCycleAsync Tests` before the file's final `#endregion`. `CreateTestGifStream()` is the fixture's existing helper — use it, do not add another.

```csharp
    /// <summary>Adds <paramref name="count"/> GIFs and returns their ids in insertion order.</summary>
    private async Task<List<int>> SeedGifsAsync(int count)
    {
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            using var stream = CreateTestGifStream();
            var gif = await _repository!.AddFromFileAsync(stream, $"seed{i}.gif", $"Seed {i}");
            ids.Add(gif.Id);
        }

        return ids;
    }

    /// <summary>Claims <paramref name="count"/> times in sequence and returns the ids dispensed.</summary>
    private async Task<List<int>> ClaimSequenceAsync(int count)
    {
        var claimed = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var gif = await _repository!.ClaimNextForCycleAsync();
            Assert.That(gif, Is.Not.Null, $"claim {i} returned null");
            claimed.Add(gif!.Id);
        }

        return claimed;
    }

    [Test]
    public async Task ClaimNextForCycleAsync_FullCycle_DispensesEveryItemExactlyOnce()
    {
        var seeded = await SeedGifsAsync(5);

        var claimed = await ClaimSequenceAsync(5);

        Assert.That(claimed, Is.EquivalentTo(seeded));
        Assert.That(claimed, Is.Unique);
    }

    [Test]
    public async Task ClaimNextForCycleAsync_PastEndOfCycle_StartsAFreshCycle()
    {
        await SeedGifsAsync(3);

        var claimed = await ClaimSequenceAsync(4);

        Assert.That(claimed.Take(3), Is.Unique, "the first cycle dispenses each item once");
        Assert.That(claimed[3], Is.AnyOf(claimed[0], claimed[1]),
            "the 4th claim comes from a fresh cycle, and cannot be the held-back 3rd item");
    }

    [Test]
    public async Task ClaimNextForCycleAsync_AcrossCycleBoundary_NeverRepeatsConsecutively()
    {
        await SeedGifsAsync(3);

        // Two full cycles plus one: every boundary in this sequence is exercised.
        var claimed = await ClaimSequenceAsync(7);

        for (var i = 1; i < claimed.Count; i++)
        {
            Assert.That(claimed[i], Is.Not.EqualTo(claimed[i - 1]),
                $"claim {i} repeated claim {i - 1} — the hold-back did not hold");
        }
    }

    [Test]
    public async Task ClaimNextForCycleAsync_HeldBackItem_StillDispensedInTheNewCycle()
    {
        var seeded = await SeedGifsAsync(3);

        var claimed = await ClaimSequenceAsync(6);

        Assert.That(claimed.Skip(3).Take(3), Is.EquivalentTo(seeded),
            "the second cycle dispenses all three, including the one held back at the boundary");
    }

    [Test]
    public async Task ClaimNextForCycleAsync_ItemAddedMidCycle_IsClaimableImmediately()
    {
        await SeedGifsAsync(3);
        await ClaimSequenceAsync(1);

        using var stream = CreateTestGifStream();
        var added = await _repository!.AddFromFileAsync(stream, "mid.gif", "Mid-cycle");

        // Two pending originals plus the new one: it must appear without any reset.
        var claimed = await ClaimSequenceAsync(3);

        Assert.That(claimed, Does.Contain(added.Id));
        Assert.That(claimed, Is.Unique);
    }

    [Test]
    public async Task ClaimNextForCycleAsync_DeletedItem_IsNeverClaimed()
    {
        var seeded = await SeedGifsAsync(3);
        await _repository!.DeleteAsync(seeded[1]);

        var claimed = await ClaimSequenceAsync(4);

        Assert.That(claimed, Does.Not.Contain(seeded[1]));
    }

    [Test]
    public async Task ClaimNextForCycleAsync_EmptyLibrary_ReturnsNull()
    {
        var result = await _repository!.ClaimNextForCycleAsync();

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClaimNextForCycleAsync_SingleItemLibrary_KeepsDispensingThatItem()
    {
        var seeded = await SeedGifsAsync(1);

        var claimed = await ClaimSequenceAsync(3);

        Assert.That(claimed, Is.EqualTo(new[] { seeded[0], seeded[0], seeded[0] }),
            "a one-item library repeats by definition; the hold-back must not starve it");
    }

    [Test]
    public async Task ClaimNextForCycleAsync_ConcurrentClaims_NeverDispenseTheSameItemTwice()
    {
        var seeded = await SeedGifsAsync(5);

        var claims = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => _repository!.ClaimNextForCycleAsync()));

        var ids = claims.Select(c => c!.Id).ToList();
        Assert.That(ids, Is.Unique, "FOR UPDATE SKIP LOCKED must stop two claims taking one row");
        Assert.That(ids, Is.EquivalentTo(seeded));
    }
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebrationGifRepositoryTests.ClaimNextForCycleAsync`
Expected: build failure — `'IBanCelebrationGifRepository' does not contain a definition for 'ClaimNextForCycleAsync'`.

- [ ] **Step 5: Write the claim algorithm**

Create `TelegramGroupsAdmin.Telegram/Repositories/RotationCycleClaim.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>A table that rotates its rows shuffle-bag style.</summary>
internal enum RotationBag
{
    BanCelebrationGifs,
    BanCelebrationCaptions
}

/// <summary>
/// Shuffle-bag rotation over a table with a nullable <c>dispensed_at</c> column: a null stamp
/// means the row is still pending in the current cycle. Shared by the ban celebration GIF and
/// caption repositories.
/// </summary>
internal static class RotationCycleClaim
{
    /// <summary>
    /// The table each bag rotates and the advisory lock serializing its cycle exhaustion. Callers
    /// name a bag, never a table, so a table can never be paired with the wrong lock key.
    /// </summary>
    private static (string Table, long AdvisoryLockKey) Resolve(RotationBag bag) => bag switch
    {
        RotationBag.BanCelebrationGifs =>
            ("ban_celebration_gifs", AdvisoryLockKeys.BanCelebrationGifCycle),
        RotationBag.BanCelebrationCaptions =>
            ("ban_celebration_captions", AdvisoryLockKeys.BanCelebrationCaptionCycle),
        // Required: TreatWarningsAsErrors turns a defaultless enum switch into a CS8524 build
        // failure. A new bag added without an arm here therefore fails at runtime, not at build.
        _ => throw new ArgumentOutOfRangeException(nameof(bag), bag, "Unknown rotation bag")
    };

    /// <summary>
    /// Claims the next id in the current cycle, starting a fresh cycle when the current one is
    /// exhausted. Returns null only when nothing is claimable — an empty table, or (vanishingly
    /// rarely) every pending row locked by concurrent claims.
    /// </summary>
    /// <param name="context">Context carrying the whole operation. All statements run in one
    /// transaction because <c>pg_advisory_xact_lock</c> is transaction-scoped.</param>
    /// <param name="bag">Which rotation to claim from.</param>
    public static async Task<int?> ClaimNextIdAsync(
        AppDbContext context,
        RotationBag bag,
        CancellationToken ct)
    {
        var (table, advisoryLockKey) = Resolve(bag);

        // Production enables retry-on-failure; the strategy re-runs this whole unit on a transient
        // failure rather than letting one surface as a skipped celebration.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            // Fast path: something is still pending in the current cycle.
            var claimed = await ClaimOneAsync(context, table, ct)
                          ?? await StartFreshCycleAndClaimAsync(context, table, advisoryLockKey, ct);

            await transaction.CommitAsync(ct);
            return claimed;
        });
    }

    private static async Task<int?> StartFreshCycleAndClaimAsync(
        AppDbContext context,
        string table,
        long advisoryLockKey,
        CancellationToken ct)
    {
        // Serialize exhaustion. Released automatically when the transaction ends.
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", [advisoryLockKey], ct);

        // Double-check: whoever held the lock before us has already reset and committed, so the
        // common outcome here is a hit — and we never reset at all.
        var claimed = await ClaimOneAsync(context, table, ct);
        if (claimed is not null)
            return claimed;

        // The row dispensed most recently. Held back from the new cycle so it cannot be dispensed
        // twice in a row across the boundary, then released once the new cycle's first item is
        // claimed. Null means no row carries a stamp, so there is nothing to rotate.
        var heldBackId = await ScalarAsync(context,
            $"""
             SELECT id FROM {table}
             WHERE dispensed_at IS NOT NULL
             ORDER BY dispensed_at DESC LIMIT 1
             """, ct);

        if (heldBackId is null)
            return null;

        var rowCount = await ScalarAsync(context, $"SELECT count(*)::int FROM {table}", ct) ?? 0;

        if (rowCount <= 1)
        {
            // A one-row library repeats by definition; holding its only row back would starve it.
            await context.Database.ExecuteSqlRawAsync(
                $"UPDATE {table} SET dispensed_at = NULL WHERE dispensed_at IS NOT NULL", ct);
            return await ClaimOneAsync(context, table, ct);
        }

        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET dispensed_at = NULL WHERE dispensed_at IS NOT NULL AND id <> {{0}}",
            [heldBackId.Value], ct);

        claimed = await ClaimOneAsync(context, table, ct);

        // Release the held-back row into the remainder of the new cycle.
        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET dispensed_at = NULL WHERE id = {{0}}", [heldBackId.Value], ct);

        // Only reachable if concurrent claims took every freshly-cleared row first.
        return claimed ?? await ClaimOneAsync(context, table, ct);
    }

    /// <summary>
    /// Picks a random pending row and stamps it in one statement. FOR UPDATE SKIP LOCKED stops two
    /// concurrent claims taking the same row.
    /// </summary>
    private static Task<int?> ClaimOneAsync(AppDbContext context, string table, CancellationToken ct) =>
        ScalarAsync(context,
            $"""
             UPDATE {table} SET dispensed_at = now()
             WHERE id = (
                 SELECT id FROM {table}
                 WHERE dispensed_at IS NULL
                 ORDER BY random() LIMIT 1
                 FOR UPDATE SKIP LOCKED
             )
             RETURNING id
             """, ct);

    /// <summary>
    /// Runs a statement that yields at most one integer, and returns it — or null when the
    /// statement matched nothing.
    ///
    /// ToListAsync materializes without composing, which is what lets an UPDATE ... RETURNING run
    /// here at all: applying any LINQ operator instead (FirstOrDefaultAsync, Where, Take) wraps the
    /// SQL in a subquery, and PostgreSQL rejects a data-modifying statement there. Do not "tidy"
    /// this into FirstOrDefaultAsync.
    /// </summary>
    private static async Task<int?> ScalarAsync(AppDbContext context, string sql, CancellationToken ct)
    {
        var results = await context.Database.SqlQueryRaw<int>(sql).ToListAsync(ct);
        return results.Count > 0 ? results[0] : null;
    }
}
```

- [ ] **Step 6: Add the repository method**

In `IBanCelebrationGifRepository.cs`, add after `GetRandomAsync`:

```csharp
    /// <summary>
    /// Claims the next GIF in the current rotation cycle: picks a random GIF not yet dispensed,
    /// marks it dispensed, and returns it. When the cycle is exhausted, starts a fresh cycle —
    /// holding back the GIF dispensed last so it cannot repeat immediately — and claims from it.
    /// Returns null only when the library is empty.
    /// </summary>
    Task<BanCelebrationGif?> ClaimNextForCycleAsync(CancellationToken ct = default);
```

In `BanCelebrationGifRepository.cs`, add this method after `GetRandomAsync` (no new `using` is needed — `RotationBag` lives in this namespace):

```csharp
    public async Task<BanCelebrationGif?> ClaimNextForCycleAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var claimedId = await RotationCycleClaim.ClaimNextIdAsync(
            context, RotationBag.BanCelebrationGifs, ct);

        if (claimedId is null)
            return null;

        var dto = await context.BanCelebrationGifs.FindAsync([claimedId.Value], ct);
        return dto?.ToModel();
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebrationGifRepositoryTests`
Expected: PASS, including every pre-existing test in the fixture.

If `ClaimNextForCycleAsync_ConcurrentClaims_NeverDispenseTheSameItemTwice` fails with a duplicate, the `FOR UPDATE SKIP LOCKED` clause is not reaching the database — check the claim statement, do not weaken the test.

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Data/Constants/AdvisoryLockKeys.cs \
        TelegramGroupsAdmin.Telegram/Repositories/RotationCycleClaim.cs \
        TelegramGroupsAdmin.Telegram/Repositories/IBanCelebrationGifRepository.cs \
        TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationGifRepository.cs \
        TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationGifRepositoryTests.cs
git commit -m "feat(celebration): claim GIFs from a database-backed rotation cycle"
```

---

### Task 3: The caption repository

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IBanCelebrationCaptionRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationCaptionRepository.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationCaptionRepositoryTests.cs`

**Interfaces:**
- Consumes: `RotationCycleClaim.ClaimNextIdAsync(AppDbContext, RotationBag, CancellationToken)` and `RotationBag.BanCelebrationCaptions` from Task 2.
- Produces: `Task<BanCelebrationCaption?> IBanCelebrationCaptionRepository.ClaimNextForCycleAsync(CancellationToken ct = default)` — Task 4 calls this.

- [ ] **Step 1: Write the failing tests**

Add to `BanCelebrationCaptionRepositoryTests.cs` in a new `#region ClaimNextForCycleAsync Tests`. The caption fixture has no seeding helper, so this adds its own:

```csharp
    /// <summary>Adds <paramref name="count"/> captions and returns their ids in insertion order.</summary>
    private async Task<List<int>> SeedCaptionsAsync(int count)
    {
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var caption = await _repository!.AddAsync(
                $"{{username}} banned #{i}", $"You were banned #{i}", $"Seed {i}");
            ids.Add(caption.Id);
        }

        return ids;
    }

    private async Task<List<int>> ClaimSequenceAsync(int count)
    {
        var claimed = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var caption = await _repository!.ClaimNextForCycleAsync();
            Assert.That(caption, Is.Not.Null, $"claim {i} returned null");
            claimed.Add(caption!.Id);
        }

        return claimed;
    }

    [Test]
    public async Task ClaimNextForCycleAsync_FullCycle_DispensesEveryItemExactlyOnce()
    {
        var seeded = await SeedCaptionsAsync(5);

        var claimed = await ClaimSequenceAsync(5);

        Assert.That(claimed, Is.EquivalentTo(seeded));
        Assert.That(claimed, Is.Unique);
    }

    [Test]
    public async Task ClaimNextForCycleAsync_AcrossCycleBoundary_NeverRepeatsConsecutively()
    {
        await SeedCaptionsAsync(3);

        var claimed = await ClaimSequenceAsync(7);

        for (var i = 1; i < claimed.Count; i++)
        {
            Assert.That(claimed[i], Is.Not.EqualTo(claimed[i - 1]),
                $"claim {i} repeated claim {i - 1} — the hold-back did not hold");
        }
    }

    [Test]
    public async Task ClaimNextForCycleAsync_ItemAddedMidCycle_IsClaimableImmediately()
    {
        await SeedCaptionsAsync(3);
        await ClaimSequenceAsync(1);

        var added = await _repository!.AddAsync("{username} mid-cycle", "You mid-cycle", "Mid");

        var claimed = await ClaimSequenceAsync(3);

        Assert.That(claimed, Does.Contain(added.Id));
        Assert.That(claimed, Is.Unique);
    }

    [Test]
    public async Task ClaimNextForCycleAsync_EmptyLibrary_ReturnsNull()
    {
        var result = await _repository!.ClaimNextForCycleAsync();

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClaimNextForCycleAsync_ConcurrentClaims_NeverDispenseTheSameItemTwice()
    {
        var seeded = await SeedCaptionsAsync(5);

        var claims = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => _repository!.ClaimNextForCycleAsync()));

        var ids = claims.Select(c => c!.Id).ToList();
        Assert.That(ids, Is.Unique, "FOR UPDATE SKIP LOCKED must stop two claims taking one row");
        Assert.That(ids, Is.EquivalentTo(seeded));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebrationCaptionRepositoryTests.ClaimNextForCycleAsync`
Expected: build failure — `'IBanCelebrationCaptionRepository' does not contain a definition for 'ClaimNextForCycleAsync'`.

- [ ] **Step 3: Add the repository method**

In `IBanCelebrationCaptionRepository.cs`, add after `GetRandomAsync`:

```csharp
    /// <summary>
    /// Claims the next caption in the current rotation cycle: picks a random caption not yet
    /// dispensed, marks it dispensed, and returns it. When the cycle is exhausted, starts a fresh
    /// cycle — holding back the caption dispensed last so it cannot repeat immediately — and
    /// claims from it. Returns null only when the library is empty.
    /// </summary>
    Task<BanCelebrationCaption?> ClaimNextForCycleAsync(CancellationToken ct = default);
```

In `BanCelebrationCaptionRepository.cs`, add this method after `GetRandomAsync` (no new `using` is needed — `RotationBag` lives in this namespace):

```csharp
    public async Task<BanCelebrationCaption?> ClaimNextForCycleAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var claimedId = await RotationCycleClaim.ClaimNextIdAsync(
            context, RotationBag.BanCelebrationCaptions, ct);

        if (claimedId is null)
            return null;

        var dto = await context.BanCelebrationCaptions.FindAsync([claimedId.Value], ct);
        return dto?.ToModel();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebrationCaptionRepositoryTests`
Expected: PASS, including every pre-existing test in the fixture.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Repositories/IBanCelebrationCaptionRepository.cs \
        TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationCaptionRepository.cs \
        TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationCaptionRepositoryTests.cs
git commit -m "feat(celebration): claim captions from a database-backed rotation cycle"
```

---

### Task 4: Switch the service over and delete the cache

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs` (constructor; `GetNextGifAsync` around line 168; `GetNextCaptionAsync` around line 205)
- Delete: `TelegramGroupsAdmin.Telegram/Services/IBanCelebrationCache.cs`
- Delete: `TelegramGroupsAdmin.Telegram/Services/BanCelebrationCache.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs:159`
- Modify: `TelegramGroupsAdmin.UnitTests/Services/BanCelebrationServiceTests.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/BanCelebrationServiceTests.cs`

**Interfaces:**
- Consumes: `ClaimNextForCycleAsync` on both repositories, from Tasks 2 and 3.
- Produces: a `BanCelebrationService` constructor with no `IBanCelebrationCache` parameter. Task 5 removes the now-orphaned `GetAllIdsAsync`.

- [ ] **Step 1: Replace both selection methods**

In `BanCelebrationService.cs`, delete `IBanCelebrationCache celebrationCache,` from the primary constructor parameter list, and replace the whole of `GetNextGifAsync` and `GetNextCaptionAsync` — including their XML doc comments — with:

```csharp
    /// <summary>
    /// Claims the next GIF in the rotation. The repository owns cycle state, so every GIF is sent
    /// once before any repeats, newly added GIFs are claimable immediately, and the rotation
    /// survives restarts.
    /// </summary>
    private Task<BanCelebrationGif?> GetNextGifAsync(CancellationToken cancellationToken) =>
        gifRepository.ClaimNextForCycleAsync(cancellationToken);

    /// <summary>
    /// Claims the next caption in the rotation. Same cycle semantics as <see cref="GetNextGifAsync"/>.
    /// </summary>
    private Task<BanCelebrationCaption?> GetNextCaptionAsync(CancellationToken cancellationToken) =>
        captionRepository.ClaimNextForCycleAsync(cancellationToken);
```

The old bodies' retry loop existed to skip IDs whose rows had been deleted since the bag was shuffled. A claim stamps and returns a row in one statement, so it cannot hand back a stale ID and the loop has nothing to do.

- [ ] **Step 2: Delete the cache and its registration**

```bash
git rm TelegramGroupsAdmin.Telegram/Services/IBanCelebrationCache.cs \
       TelegramGroupsAdmin.Telegram/Services/BanCelebrationCache.cs \
       TelegramGroupsAdmin.UnitTests/Services/BanCelebrationCacheTests.cs
```

(`BanCelebrationCacheTests.cs` exists only if this branch is based on work that added it; if `git rm` reports it missing, that is fine — drop it from the command.)

Then delete this line from `ServiceCollectionExtensions.cs` (line 159):

```csharp
            services.AddSingleton<IBanCelebrationCache, BanCelebrationCache>(); // Singleton: shuffle-bag state for ban celebrations
```

- [ ] **Step 3: Rework the unit-test fixture**

`TelegramGroupsAdmin.UnitTests/Services/BanCelebrationServiceTests.cs` builds the service with a real `BanCelebrationCache` and mocks `GetAllIdsAsync` in roughly twenty places. Rework it:

1. Delete the `private BanCelebrationCache _celebrationCache = null!;` field, its initialization in `SetUp`, and the constructor argument.
2. Replace every `_mockGifRepository.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });` with a claim setup returning a GIF directly:

```csharp
        _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif" });
```

and every `_mockCaptionRepository.GetAllIdsAsync(...)` with:

```csharp
        _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationCaption { Id = 1, Text = "{username} banned", DmText = "You were banned" });
```

Match each call site's existing intent: where the old setup returned an empty list to mean "library empty", return `(BanCelebrationGif?)null`. Where a test previously stubbed `GetByIdAsync` to feed the bag's dequeue, that stub is now redundant — the claim returns the row itself.

3. Delete outright the two tests that assert on bag mechanics rather than service behavior: the one asserting `GetAllIdsAsync` was received twice (initial load plus reshuffle, around line 234) and the one asserting it was received once to populate an empty cache (around line 265). Rotation is the repository's business now and is covered by Tasks 2 and 3's integration tests. Do not rewrite them against the new method — that would be testing the mock.

Keep every test about config gating, trigger types, placeholder replacement, DM delivery, username masking, and `file_id` caching. Those are the service's actual responsibilities and must still pass unchanged.

- [ ] **Step 4: Update the integration fixture**

In `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/BanCelebrationServiceTests.cs`, delete the `services.AddSingleton<IBanCelebrationCache, BanCelebrationCache>();` registration (around line 139) and the `using TelegramGroupsAdmin.Telegram.Services;` import if nothing else in the file needs it. Every test in that fixture drives the service through real repositories and must otherwise pass unchanged.

- [ ] **Step 5: Run the affected suites**

Run:
```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~BanCelebration
dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebrationServiceTests
```
Expected: PASS. A failure in a config/placeholder/DM test means the rework changed behavior it should not have — fix the rework, not the test.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(celebration): claim from the database and delete the in-memory bag"
```

---

### Task 5: Remove the orphaned `GetAllIdsAsync`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IBanCelebrationGifRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationGifRepository.cs:61`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IBanCelebrationCaptionRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationCaptionRepository.cs:37`
- Modify: `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationGifRepositoryTests.cs`, `BanCelebrationCaptionRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 4's removal of the service's only calls to `GetAllIdsAsync`.
- Produces: nothing new. Purely a deletion.

- [ ] **Step 1: Confirm it is genuinely orphaned**

Run: `grep -rn "GetAllIdsAsync" --include=*.cs --include=*.razor . | grep -v obj`
Expected: hits only in the two interfaces, the two implementations, and their own tests. If anything else calls it, stop and report — the plan assumed otherwise.

- [ ] **Step 2: Delete the method from both interfaces and both implementations**

Remove the `GetAllIdsAsync` declaration and its XML doc from `IBanCelebrationGifRepository.cs` and `IBanCelebrationCaptionRepository.cs`, and the implementation from both repository classes.

- [ ] **Step 3: Delete its tests**

Remove the `#region GetAllIdsAsync Tests` blocks (and any individual `GetAllIdsAsync_*` tests outside a region) from both integration test fixtures.

- [ ] **Step 4: Verify the build and the fixtures**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~BanCelebration`
Expected: 0 warnings, 0 errors; all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(celebration): drop the orphaned GetAllIdsAsync"
```

---

### Task 6: Full build and test sweep

**Files:**
- Modify: any file the compiler flags as still referencing the deleted cache or method.

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces: a green build and test run across the solution.

- [ ] **Step 1: Build the solution**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: 0 warnings, 0 errors. Any `IBanCelebrationCache` reference the earlier tasks missed surfaces here — delete it; do not reintroduce the type.

- [ ] **Step 2: Run the unit and component projects**

Run: `dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.ComponentTests`
Expected: PASS. Report any pre-existing failure rather than fixing unrelated tests.

- [ ] **Step 3: Run the full integration suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests`
Expected: PASS. The full suite, not just the ban celebration filter — the migration touches shared schema and the canonical dataset, so the blast radius is wider than this feature.

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "chore(celebration): sweep up references to the removed rotation cache"
```

Skip this commit if Steps 1–3 needed no changes.

---

## Verification

The behavior this plan delivers, as a reviewer can check it by hand: with the app running and several GIFs in the library, trigger a ban, add a new GIF in Settings → Ban Celebration, then trigger further bans. The new GIF appears within the current cycle rather than after it. Restart the app mid-cycle and keep triggering bans: the rotation resumes where it left off instead of reshuffling, and no GIF repeats until the cycle completes.
