# Ban celebration — move the shuffle bag from memory into the database

## Problem

Ban celebration rotation state lives in `BanCelebrationCache`, a process singleton holding one
`Queue<int>` of database IDs per content type. `BanCelebrationService` refills a queue from the
database only when it runs dry (`BanCelebrationService.cs:170`, `:207`), dequeues one ID per
celebration, and skips IDs whose rows no longer resolve.

Two sources of truth — rows in Postgres, a queue in memory — produce four problems:

1. **New content is invisible until the bag drains.** A GIF uploaded in Settings does not enter
   the rotation until every ID already in the bag has been dispensed. With ~100 GIFs and a quiet
   week, that is many bans away. This was the original complaint.
2. **The in-memory bag has to be told about new rows, and the telling can race.** PR #530
   attempted exactly that — splicing new IDs into the live bag — and review found a window around
   rehydration: the service checks `IsGifBagEmpty`, awaits `GetAllIdsAsync`, then repopulates,
   while a concurrent upload commits its row and notifies. Depending on the interleaving the new
   GIF is either missed for a cycle (harmless — it degrades to problem 1) or spliced into a bag
   that already contains it, repeating a GIF inside one cycle. Every candidate repair — a
   semaphore spanning the refill, or pending-set bookkeeping inside the cache — patches a problem
   that exists only because the bag is a separate copy of the data.
3. **Restarts re-randomize the rotation.** The bag is in memory, so a restart discards it. The
   next celebration reshuffles the whole library, and a GIF shown minutes before the restart can
   appear again immediately. "No repeats until everything has been shown" silently does not hold
   across restarts.
4. **Backup restore leaves the bag stale.** The restored database has different rows; the
   in-process bag still holds the previous database's IDs. It drains harmlessly, but rotation
   state does not travel with the backup.

## Approach: the rows are the bag

Add a nullable `dispensed_at` column to `ban_celebration_gifs` and `ban_celebration_captions`.
`NULL` means "not yet dispensed in the current cycle" — that is, in the bag. Claiming an item is
a single statement that picks a random undispensed row and stamps it. Exhausting the cycle is a
single statement that clears every stamp.

This deletes the notification problem rather than fixing it. A row inserted with `dispensed_at`
`NULL` **is** in the bag at the instant of commit, so there is nothing to notify, no ordering rule
about where a notify call sits relative to the row-deleting failure paths in `AddFromFileAsync`,
and no empty-bag edge to race against. Problems 3 and 4 disappear for the same reason: the state
lives where the data lives.

The shuffle-bag guarantee is preserved exactly. "Every item shown once before any repeat" becomes
the meaning of the column rather than an invariant maintained by hand, and picking uniformly at
random among the undispensed rows yields the same uniformly random cycle ordering that
Fisher-Yates produced up front.

### The claim

```sql
UPDATE ban_celebration_gifs SET dispensed_at = now()
WHERE id = (
    SELECT id FROM ban_celebration_gifs
    WHERE dispensed_at IS NULL
    ORDER BY random() LIMIT 1
    FOR UPDATE SKIP LOCKED
)
RETURNING id;
```

`FOR UPDATE SKIP LOCKED` is load-bearing: two bans processed concurrently would otherwise both
select the same row and both dispense it. With it, the second claim skips the locked row and takes
another.

Zero rows back means the cycle is exhausted, and the exhaustion path is where the two remaining
correctness problems live. Both are closed below; neither costs anything on the hot path, which
stays exactly the one statement above.

### Exhaustion: serialize it with an advisory lock

Two celebrations reaching exhaustion together would both reset. That is worse than it first
appears: if A has claimed a row but not yet committed, B's `SKIP LOCKED` skips A's row, B
concludes the cycle is exhausted, and B's reset blocks on A's row lock and then clears A's stamp
the instant A commits — A's GIF was dispensed and is immediately back in the bag.

A transaction-scoped advisory lock, taken only after a claim comes back empty, serializes the
whole exhaustion path:

```sql
SELECT pg_advisory_xact_lock(<key>);
```

Then **re-run the claim before resetting**. The loser of the race blocks on the lock, and by the
time it acquires it the winner has already reset and committed, so the re-claim succeeds and the
loser never resets at all. Only when the re-claim is also empty does a reset happen. The lock
releases automatically at commit or rollback — no cleanup path and nothing to leak if the
transaction dies.

Rejected here: `SERIALIZABLE` isolation, which turns the race into a serialization failure and
pushes retry loops into the application; and a sentinel row locked with `SELECT ... FOR UPDATE`,
which is the same semantics as the advisory lock but needs a table to hold the sentinel.

### Reset: hold back the last-dispensed row

Even single-threaded, a plain reset lets the last item of cycle N be the first of cycle N+1 — the
one repeat a viewer actually notices. The current in-memory implementation has this property too
(a fresh shuffle is independent of the previous cycle's tail), and it is cheap to do better than
parity here. Reset everything *except* the most recently dispensed row, claim from the fresh
cycle — which therefore cannot pick it — then release it for the remainder of the cycle:

```sql
UPDATE ban_celebration_gifs SET dispensed_at = NULL
WHERE dispensed_at IS NOT NULL
  AND id <> (SELECT id FROM ban_celebration_gifs
             WHERE dispensed_at IS NOT NULL
             ORDER BY dispensed_at DESC LIMIT 1);
-- claim (cannot pick the held-back row)
UPDATE ban_celebration_gifs SET dispensed_at = NULL WHERE dispensed_at IS NOT NULL;
```

Guard: a single-row library has nothing else to pick, so skip the hold-back when the library holds
one row — that library repeats by definition. A two-row library degrades to strict alternation,
which is the best available.

Both statements run inside the same transaction as the claim, under the advisory lock, on a path
that executes once per full cycle — roughly once per hundred celebrations at the current library
size.

### The resulting guarantee

Every item is dispensed exactly once per cycle; no item is dispensed twice in succession across a
cycle boundary; concurrent bans never dispense the same item; and rotation position survives
restarts and travels with a backup. That is strictly stronger than what the in-memory bag provides
today on all four counts.

### Scale escape hatch, not needed now

The reset is an N-row `UPDATE`. If a library ever reached tens of thousands of rows, the cheaper
shape is a generation counter — `last_cycle int` per row plus a single-row `current_cycle` table,
where advancing the cycle is one atomic `UPDATE ... RETURNING` and no mass write happens at all.
At ~100 rows that is a table and a subquery bought for nothing, so it stays unbuilt and recorded
here as the known next move.

### Rejected: keeping the in-memory bag and fixing the race

Considered and dropped: a `SemaphoreSlim` making "refill" and "commit + notify" mutually exclusive
(correct, but the gate has to span a database write and reaches across the repository and the
service), and a pending-set plus duplicate guard inside the cache (correct and self-contained, but
it is repair logic for a problem that a single source of truth does not have). Both leave problems
1, 3 and 4 partly or wholly in place. Both also keep a growing in-memory structure — the library
has no upper bound and is at ~100 GIFs today.

### Rejected: never letting the bag go empty

Refilling the moment the last item is pulled does not help: the refill is asynchronous, so the bag
is still empty for the duration of the `SELECT`. Making "never empty" literally true requires
double-buffering a prefetched next cycle — more machinery than the database column, for a strictly
worse result.

## Implementation

### Schema

`BanCelebrationGifDto` and `BanCelebrationCaptionDto` each gain:

```csharp
    /// <summary>
    /// When this item was dispensed in the CURRENT rotation cycle, or null if it is still
    /// pending. Cleared for every row when the cycle is exhausted — this is cycle state, not a
    /// durable "last shown" record.
    /// </summary>
    [Column("dispensed_at")]
    public DateTimeOffset? DispensedAt { get; set; }
```

Configure in `AppDbContext` (Fluent API, per the repository's schema convention), then
`dotnet ef migrations add AddBanCelebrationDispensedAt`. Apply with `dotnet run --migrate`.

No index. At ~100 rows per table the planner scans regardless, and a partial index on
`dispensed_at IS NULL` would cost more to maintain than it saves. Revisit only if a library ever
reaches a scale where `EXPLAIN` says otherwise.

Existing rows — including the canonical test dataset's 92 GIFs and 74 captions — take `NULL` and
are therefore all pending, which is the correct starting state for a fresh cycle.

### Naming a rotation

Two tables share one algorithm, so the algorithm needs to know which table it is rotating. It is
told by an enum, not a string — callers name a rotation and never a table:

```csharp
internal enum RotationBag
{
    BanCelebrationGifs,
    BanCelebrationCaptions
}
```

A single switch inside the helper maps each bag to its table name and its advisory lock key, so a
table can never be paired with the wrong key, and the only strings interpolated into SQL are two
constants that no caller can reach. The switch needs an explicit `_ => throw` arm: the project sets
`TreatWarningsAsErrors`, and a defaultless switch expression over an enum fails the build with
CS8524 (verified). The consequence is worth knowing — a future third rotation added without a
switch arm throws at runtime rather than breaking the build, and its own tests are what catch it.

Rejected: deriving the table name from EF model metadata (`FindEntityType(...).GetTableName()`).
It removes the hand-typed constant, but the drift it guards against — someone renaming a `[Table]`
attribute — already fails loudly, since every claim would error against a table that no longer
exists and every rotation test would fail on the next run. Reflection is not worth buying that.

Advisory lock keys keep their own registry, because the key space is global to the database and a
collision between unrelated features is only visible if every key is written in one place. New file
`TelegramGroupsAdmin.Data/Constants/AdvisoryLockKeys.cs`, beside the existing
`MigrationCompactionConstants`:

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

Literal constants rather than `hashtext('...')` at runtime: the values are greppable, stable
across Postgres versions, and collisions are checked by reading one file.

### Repositories

Both interfaces gain one method:

```csharp
    /// <summary>
    /// Claims the next item in the current rotation cycle: picks a random item not yet dispensed,
    /// marks it dispensed, and returns it. When the cycle is exhausted, starts a fresh cycle —
    /// holding back the item dispensed last so it cannot repeat immediately — and claims from it.
    /// Returns null only when the library is empty.
    /// </summary>
    Task<BanCelebrationGif?> ClaimNextForCycleAsync(CancellationToken ct = default);
```

One transaction per call: claim; on empty, take the advisory lock, re-claim, and only then reset
with the hold-back and claim again; finally load the claimed row through EF by ID. A second empty
result after the reset means the library itself is empty — return null, and the service skips the
celebration exactly as it does today.

**Settled by spike, not assumed** (throwaway integration tests against Postgres 18, EF Core 10 +
Npgsql, including a context configured with `EnableRetryOnFailure` exactly as production is):

- Statements stay inside EF's raw SQL APIs. Dropping to a raw `DbCommand` off
  `context.Database.GetDbConnection()` was tried and rejected: it works, but it bypasses EF's
  command interception and logging — which is what feeds Seq and OpenTelemetry here — and takes the
  operation outside the execution strategy. That is a real cost paid to guard against a
  hypothetical future edit.
- A data-modifying CTE **cannot** be nested: `0A000: WITH clause containing a data-modifying
  statement must be at the top level`. So a CTE does not make an `UPDATE` composition-safe, and
  none is used — the claim is a plain `UPDATE ... RETURNING id`.
- `context.Database.SqlQueryRaw<int>(sql).ToListAsync(ct)` sends that SQL verbatim and returns the
  `RETURNING` id. **Never apply a LINQ operator to it** — `FirstOrDefaultAsync` wraps it in a
  subquery and Postgres rejects the `UPDATE`. Materializing with `ToListAsync` is not composition;
  operators are.
- The `AS "Value"` alias that `SqlQuery` scalars are usually written with is **not** required here:
  verified positively by asserting the returned id equals the row the statement stamped.
- `ExecuteSqlRawAsync` runs the advisory lock and the reset statements, binding values as `{0}`
  parameters. Only table names are interpolated, and only from internal constants.
- An explicit `BeginTransactionAsync` does **not** throw under the production retrying execution
  strategy — verified directly, since that is a live-only failure the test fixtures would hide
  (they configure no retry). The claim is still wrapped in
  `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, not to avoid an exception but so
  a transient failure retries the whole claim instead of surfacing as a skipped celebration.
- `pg_advisory_xact_lock` verified held on its transaction and released on commit, with no cleanup
  path. It is scoped to that transaction, so every statement in a claim must run through the same
  context — a call landing on a different pooled connection would lock nothing.
- `FOR UPDATE SKIP LOCKED` verified under this shape: three concurrent claims over three rows
  returned three distinct rows.

`GetAllIdsAsync` loses its only callers and is deleted from both interfaces and implementations
along with its tests. `GetRandomAsync` stays — `BanCelebrationSettings.razor:473-474` uses it for
the settings-page preview, which deliberately does not participate in the rotation.

### Service

`BanCelebrationService.GetNextGifAsync` and `GetNextCaptionAsync` collapse to one repository call
each. The check-empty/reload/dequeue loop goes, and so does the skip-deleted-ID retry: a claim
returns a row that existed and was stamped in the same statement, so it cannot be a stale ID. The
`IBanCelebrationCache` constructor dependency goes with them.

### Deletions

- `IBanCelebrationCache`, `BanCelebrationCache`, and the DI registration in
  `ServiceCollectionExtensions.cs:159`.
- `GetAllIdsAsync` on both repository interfaces and implementations.
- Nothing in the UI changes.

## Testing

**Integration — repositories (the substance of this change):**

- Claiming across a full cycle returns every row exactly once before any repeats.
- The cycle resets automatically: with N rows, N+1 claims succeed and the (N+1)th comes from a
  fresh cycle.
- The hold-back holds: across a cycle boundary the first item of the new cycle is never the last
  item of the old one. With 3 rows, drain twice over and assert no two consecutive claims match —
  deterministic, since the hold-back makes the adjacent repeat impossible rather than unlikely.
- The held-back item is not lost: it is still dispensed within the new cycle.
- A single-row library claims that row repeatedly rather than returning null — the hold-back guard
  must not starve it.
- Concurrent exhaustion: run two claims in parallel against a cycle with one row left and assert
  both succeed, return different rows, and that exactly one reset occurred (assert on the count of
  rows whose stamp survived, not on timing).
- A row inserted mid-cycle is claimable immediately — the property the whole change exists for.
  Assert it is claimed within the current cycle, without any reset having occurred.
- A deleted row is never claimed.
- An empty library returns null rather than looping or throwing.
- Concurrent claims: fire N claims in parallel against N rows and assert N distinct results. This
  pins `FOR UPDATE SKIP LOCKED`; without it the test fails on duplicate claims.

**Integration — `BanCelebrationServiceTests`:** the existing fixture keeps working against real
repositories. The mid-cycle test from the closed PR becomes trivially true and stays, minus the
refill-counting decorator, which has nothing left to count.

**Unit — `BanCelebrationServiceTests`:** roughly twenty `GetAllIdsAsync` mock setups become
`ClaimNextForCycleAsync` setups returning a row directly. This is mechanical but touches most of
the fixture; expect the diff to be dominated by it.

**Deleted:** `BanCelebrationCacheTests` in full.

## Out of scope

- The settings UI, which is unchanged.
- Surfacing `dispensed_at` to admins. It is cycle state, not history; a "last shown" feature would
  need its own durable column.
- A pre-existing defect noticed while tracing this: `AddFromFileAsync` inserts its row before
  writing the file, so a celebration firing in that gap can draw a GIF that is not yet on disk,
  log "GIF file not found on disk", and skip the celebration. Unchanged by this work and worth its
  own issue.
