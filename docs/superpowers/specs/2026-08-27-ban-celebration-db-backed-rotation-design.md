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
WITH claimed AS (
    UPDATE ban_celebration_gifs SET dispensed_at = now()
    WHERE id = (
        SELECT id FROM ban_celebration_gifs
        WHERE dispensed_at IS NULL
        ORDER BY random() LIMIT 1
        FOR UPDATE SKIP LOCKED
    )
    RETURNING id
)
SELECT id FROM claimed;
```

`FOR UPDATE SKIP LOCKED` is load-bearing: two bans processed concurrently would otherwise both
select the same row and both dispense it. With it, the second claim skips the locked row and takes
another.

Zero rows back means the cycle is exhausted. The repository then clears the cycle and claims
again, in the same transaction:

```sql
UPDATE ban_celebration_gifs SET dispensed_at = NULL WHERE dispensed_at IS NOT NULL;
```

A second empty result after the reset means the library itself is empty; the method returns null
and the service skips the celebration, as it does today.

### What this deliberately does not solve

Two celebrations that exhaust the cycle at the same instant can both reset, so an item dispensed
by one may become claimable by the other and appear twice in quick succession. This is the same
property the current implementation has at every cycle boundary — a fresh shuffle is independent
of the previous cycle's tail, so the last GIF of cycle N can be the first of cycle N+1 — and it
is not made worse here. Adding cycle-generation bookkeeping to close it would reintroduce the
complexity this design exists to remove.

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

### Repositories

Both interfaces gain one method:

```csharp
    /// <summary>
    /// Claims the next item in the current rotation cycle: picks a random item not yet dispensed,
    /// marks it dispensed, and returns it. When the cycle is exhausted, clears it and claims from
    /// the fresh cycle. Returns null only when the library is empty.
    /// </summary>
    Task<BanCelebrationGif?> ClaimNextForCycleAsync(CancellationToken ct = default);
```

Implementation runs the claim, the conditional reset, and the retry inside one transaction, then
loads the claimed row through EF by ID.

**Implementation risk to settle empirically, not by assumption:** EF composes `FromSql` /
`Database.SqlQuery<T>` into a subquery, which is why the claim is written as a data-modifying CTE
wrapped in a `SELECT` — valid to nest, unlike a bare `UPDATE`. The plan must verify this executes
under EF Core 10 + Npgsql with an integration test before the rest is built. If composition breaks
it, fall back to a raw `DbCommand` from `context.Database.GetDbConnection()` with
`ExecuteScalarAsync`, which has no composition behavior at all.

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
