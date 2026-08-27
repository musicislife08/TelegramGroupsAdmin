# Ban celebration — newly added GIFs and captions join the shuffle bag immediately

## Problem

`BanCelebrationCache` (singleton, `TelegramGroupsAdmin.Telegram/Services/BanCelebrationCache.cs`)
holds a shuffle bag per content type: a `Queue<int>` of IDs drawn from the database, shuffled
Fisher-Yates, dequeued one per celebration. The bag is only ever refilled when it runs dry —
`BanCelebrationService.GetNextGifAsync` (`BanCelebrationService.cs:170`) checks
`IsGifBagEmpty` and calls `RepopulateGifBag(await gifRepository.GetAllIdsAsync(ct))`.
`GetNextCaptionAsync` does the same for captions.

The consequence: a GIF or caption added through the settings UI is invisible to the rotation
until the current bag drains. With a 30-GIF library and a quiet week, a freshly uploaded GIF
may not appear for many bans. The only ways to force it in today are restarting the app
(the bag is in-memory, so it starts empty) or waiting the bag out.

Deletion has no equivalent problem. `GetNextGifAsync` fetches each dequeued ID and, when the
row is gone, logs and moves to the next item in the bag (`BanCelebrationService.cs:195`).
Stale IDs are skipped, so nothing is needed on the delete path.

## Approach: splice new IDs into the live bag

Add to `IBanCelebrationCache`:

```csharp
void AddGifId(int id);
void AddCaptionId(int id);
```

Each takes the type's lock and:

- **If the bag is empty, does nothing.** An empty bag means the next celebration reloads every
  ID from the database, and that reload already includes the new row. Inserting here would
  instead guarantee the new item is dispensed first and only then trigger a full reshuffle —
  a subtly different rotation for no benefit.
- **Otherwise, splices the ID in at a uniformly random position** among the items still pending.
  `Queue<int>` has no random insert, so the implementation drains the queue into a `List<int>`,
  inserts at `Random.Shared.Next(list.Count + 1)`, and re-enqueues in order.

Splicing rather than clearing is the point of the design. Clearing the bag would restore the
whole library to "not yet shown", so items dispensed earlier in the current cycle could repeat
long before the rest of the library has been seen — exactly the property the shuffle bag exists
to guarantee. Splicing preserves it: everything still pending stays pending, nothing already
shown returns early, and the new item lands somewhere in the remainder of the current cycle.

Bags are bounded by library size (tens of items), so the drain-and-refill cost is irrelevant
next to the per-ban database and Telegram calls.

### Rejected: a "reshuffle now" button in the settings UI

The original framing of this change was a button on the ban celebration settings page that
repopulated the bag on demand. Automatic insertion makes it redundant — new content is live the
moment it is added, with no admin action and nothing to explain in the UI. No UI change is part
of this work.

### Rejected: invalidating from the Blazor component

`BanCelebrationSettings.razor` could notify the cache after its add dialogs return, but
`AddBanCelebrationGifDialog` closes with `DialogResult.Ok(true)` and would have to be changed to
carry the new ID back. It would also cover only the UI path. The repository already owns ID
creation and is the single funnel every caller passes through.

## Implementation

### `IBanCelebrationCache` / `BanCelebrationCache`

Two new methods, mirroring the existing `RepopulateGifBag` / `RepopulateCaptionBag` pair in
structure and locking. `AddGifId` takes `_gifLock`; `AddCaptionId` takes `_captionLock`.

### `BanCelebrationGifRepository`

Inject `IBanCelebrationCache`. In `AddFromFileAsync`, call `AddGifId(dto.Id)` at the very end —
after `dto.FilePath = relativePath` and its `SaveChangesAsync`, immediately before
`return dto.ToModel()`. Position matters: the DB row is created up front to obtain an ID, and
the video-conversion failure paths remove that row again
(`BanCelebrationGifRepository.cs:175`), so an earlier call could publish an ID that is about
to be deleted.

`AddFromUrlAsync` delegates to `AddFromFileAsync` and needs no change of its own.

### `BanCelebrationCaptionRepository`

Inject `IBanCelebrationCache`. In `AddAsync`, call `AddCaptionId(dto.Id)` after
`SaveChangesAsync`, before `return dto.ToModel()`.

Both repositories are scoped and the cache is a singleton in the same process, so the injection
is a standard scoped-consumes-singleton relationship. Both live in
`TelegramGroupsAdmin.Telegram`, so no new project reference is involved.

## Testing

**Unit — `BanCelebrationCache` (new test file, `TelegramGroupsAdmin.UnitTests`):**

- `AddGifId` on a non-empty bag: every previously pending ID is still dispensed exactly once,
  and the new ID is dispensed exactly once, within the same cycle.
- `AddGifId` on an empty bag: bag stays empty, `IsGifBagEmpty` remains true, `GetNextGifId`
  returns null — proving the next celebration still takes the full-reload path.
- The same two cases for `AddCaptionId`.

**Integration — existing repository tests:** `BanCelebrationGifRepositoryTests` and
`BanCelebrationCaptionRepositoryTests` need their repository construction updated for the new
constructor argument. Add one assertion per repository that adding a row notifies the cache
with the new row's ID.

**Integration — `BanCelebrationServiceTests`:** the end-to-end property worth pinning is that a
GIF added mid-cycle is sent before the bag reshuffles. Cover it if the existing fixtures make
it cheap; the unit tests above already cover the mechanism.

## Out of scope

- Any change to the settings UI.
- Delete-path handling (already correct via skip-and-continue).
- Restore-from-backup, which replaces the database wholesale; the bag drains naturally after it.
