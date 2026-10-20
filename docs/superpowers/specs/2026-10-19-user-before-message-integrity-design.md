# User Before Message: Rename Audit, Reply Targets, and the messages → telegram_users Key

**Date:** 2026-10-19
**Scope:** Make "a user exists before anything keys off them" hold everywhere. Three bugs share one
cause: code that records something about a user or a message before the row it depends on exists.
**PR target:** `develop`. No tracking issue (fixed in this branch). The related follow-up for
updates discarded at startup is #582 and is out of scope.

---

## Context

The dependency order is: a Telegram user exists → the user posts messages → things happen to
messages (detections, edits, translations, moderation audit rows). Three places break it.

### 1. A rename seen on a new message is lost

`MessageProcessingService.HandleNewMessageAsync` records the sender's names first
(`IUserIdentityService.ObserveAsync`, `MessageProcessingService.cs:210-219`) and inserts the
message much later (`:584`; service messages at `:267`). When the names changed,
`TelegramUserRepository.GetOrUpdateAsync` writes, in **one transaction**, the new names, a
`username_history` row, and a `ProfileChange` `user_actions` row carrying `(MessageId, ChatId)` of
the triggering message (`TelegramUserRepository.cs:120-146`). `user_actions` has a composite FK
`(message_id, chat_id) → messages` (`AppDbContext.cs:154-159`). The message row does not exist yet,
so the FK rejects the audit row and the transaction rolls back: the new names and the history row
are discarded too. `ObserveAsync` catches the exception, logs `Failed to record observed names`, and
returns the observed names for the rest of the update, so moderation still works. But every later
lookup of the user (alerts, ban celebration, web UI) shows the old name.

Every rename first seen on a new message is lost this way. Renames seen on joins and profile scans
pass `MessageId: null` and are unaffected. Edits pass the edited message's id; the edited message
is usually stored, but not always (`MessageEditProcessor.cs:66-73` handles the unknown-message case).

The deeper defect is that the audit row can roll back the change it describes. An audit entry
records what did happen: it is written after the action, and a failure to write it never undoes
or blocks the action.

### 2. Moderating a user the bot never observed

Every reply-target moderation command looks the target up by id with `ResolveAsync`, which reads
only: `SpamCommand.cs:63`, `BanCommand.cs:77`, `TempBanCommand.cs:69`, `WarnCommand.cs:71`,
`MuteCommand.cs:68`, `TrustCommand.cs:85`, `UnbanCommand.cs:62`. `MessageBackfillService` does the
same for the replied-to message's author (`MessageBackfillService.cs:62`). If the bot never
processed any update from that user (the typical case: the spam arrived while the app was
restarting, see #582), no `telegram_users` row exists. Then:

- the backfilled message is stored with a `user_id` that has no `telegram_users` row (an orphan);
- `SetBanStatusAsync` logs `Cannot set ban status for unknown user` and does nothing
  (`TelegramUserRepository.cs:1048-1063`);
- the ban, delete and untrust audit rows fail `FK_user_actions_telegram_users_user_id` (swallowed
  by `SafeAuditAsync`, so the Telegram actions still succeed).

The user is banned on Telegram in every managed chat, but the app has no record of it: not on the
Banned tab, and a chat added later does not apply the ban.

The replied-to message's `From` is a full Telegram user object with the user's current names (the
Bot API sends user objects as they are when the update is generated). It is an observation, and
the identity rule (`.claude/rules/user-identity.md`) says an update that shows a user's names goes
through `ObserveAsync`.

### 3. `messages.user_id` has no foreign key

`messages.user_id` is a plain column (`MessageRecordDto.cs:15-16`); nothing stops an orphan. On a
long-running production instance, orphans are a handful of rows: backfilled spam from the path in
(2), and service messages written by older pipeline versions before the observe-first ordering
(`f15d89a9`). The canonical dataset carries one too (message 211168, see Testing).

User 0 is the sender recorded for manual training samples (`DetectionResultsRepository.cs:285-317`,
chat 0) and for backfilled messages with no sender (channel posts). No code creates a user 0 row;
an install that has one got it from older code. A fresh install has none.

## Goals

1. A rename seen on a new message, a service message or an edit is recorded (names + history), and
   its `ProfileChange` audit row links the message when that message row exists.
2. An audit write never rolls back, blocks, or precedes the action it records.
3. Moderating a user from a reply records that user (row + current names) before acting, so ban
   status and audit rows persist.
4. No message can reference a user who has no row: enforced by a foreign key, with existing data
   repaired by the migration (and by the backup-restore conversion for older backups).

## Non-goals

- Processing updates queued during a restart (`DropPendingUpdates`) and stale-callback guards: #582.
- Moving the main message insert earlier in the pipeline. Not needed once the audit is written after
  the insert; the reordering of enrichment steps is a separate refactor.
- Fetching real names for repaired orphan rows (`getChatMember` from a migration is impossible, and a
  one-off job is not worth it). Names fill in at the next observation.
- Any change to the web UI.

## Anti-goals

- An audit row that can undo or block its action, or that is written before the action succeeded.
- An orphan repair keyed on hard-coded ids, or that marks a user banned without evidence the app
  itself banned them.
- Seeding test preconditions (see Testing).
- Production server details (hosts, real ids, group names, log excerpts) in the spec, plan, commits
  or PR.

---

## Design

### A. Rename audit after the action

**Repository.** `GetOrUpdateAsync` keeps names, watermark and `username_history` in its transaction
and stops writing the `ProfileChange` row. It still returns the previous names
(`ObservedNamesResult.Renamed`). It no longer needs `ProfileChangeContext`; drop the parameter if
nothing else uses it.

**Identity service.** `ObserveAsync` returns a result that carries the rename to the caller instead
of only a `UserIdentity`:

- `ObserveAsync(ObservedUser, ProfileChangeContext, ct)` returns `ObservationResult { Identity, PendingRename? }`
  (names are the builder's; a record is fine).
- `PendingRename` holds what the audit needs: user id, previous names, observed names, chat.
- A new `IUserIdentityService.AuditRenameAsync(PendingRename, int? messageId, ct)` writes the
  `ProfileChange` row through `IUserActionsRepository.InsertAsync` (reason from
  `ProfileChangeReason.Build`, chat tag via `AuditReason.WithChatTag`, actor
  `Actor.ProfileDiffDetection`, `IssuedAt` = now). It catches and logs a failure as a warning,
  following `BotModerationService.SafeAuditAsync`, and never throws except on cancellation.
- When the context has no message (`ProfileChangeContext.MessageId == null`: joins, chat-member
  refresh, profile scan, bot self-observation), `ObserveAsync` calls `AuditRenameAsync` itself right
  after the rename commits and returns `PendingRename = null`. Only callers that pass a message id
  get a pending rename back, and they write it after the message exists.

(My call: callers without a message keep their current one-line call and cannot forget the audit.)

The inline rename rescan (`UserIdentityService.cs:41-60`) is unchanged and still runs inside
`ObserveAsync` after the names commit.

**Callers with a message:**

| Caller | Where the audit goes | `messageId` passed |
|---|---|---|
| `MessageProcessingService` normal message | right after `InsertMessageAsync` (`:584`), before the already-banned early return (`:605-639`) so it is never skipped | the message id |
| `MessageProcessingService` service message | after the conditional insert (`:267`); if no row was inserted (`serviceMessageText == null`), audit with no message | the message id if inserted, else `null` |
| `MessageEditProcessor` | after `GetMessageAsync` (`:66`) | the message id if `oldMessage != null`, else `null` |
| any path that returns early before storing | audit with no message on that path | `null` |

`ObserveAsync`'s existing failure handling stays: if recording the names fails, it logs and returns
the observed names with `PendingRename = null`.

**Decided: no fallback for a failed observation.** With the audit out of the transaction,
`GetOrUpdateAsync` fails only when the database does. A later message insert would then fail its
new foreign key instead of storing an orphan, but a database that just rejected the observation
rejects the insert too. Designing for a database that is down for one statement and up for the next
is not grounded, so no "ensure an id-only row" step is added before message inserts.

### B. Reply-target moderation observes the target

Each reply-target command calls `ObserveAsync` on `message.ReplyToMessage.From` instead of
`ResolveAsync(replyFrom.Id)`: `SpamCommand`, `BanCommand` (reply branch), `TempBanCommand`,
`WarnCommand`, `MuteCommand`, `TrustCommand` (reply branch), `UnbanCommand`. The later
`ResolveAsync(targetUser.Id)` re-reads in `UnbanCommand.cs:89` and `TempBanCommand.cs:127` stay
(they read state after an action).

- **Source:** a new `ObservationSource.ReplyTarget`, documented "records only": `RescansRename`
  returns false for it, like `ChatMember` and `UserApiScan`. A moderator is already acting on the
  user, so a rename rescan would only race the action.
- **Time:** `observedAt` = the command message's `Date`. The names in `ReplyToMessage.From` are
  current as of the command's update, not the replied-to message's date. (Not the replied-to
  message's `Date`: an old message's date would let a stale watermark win, or lose to a newer one,
  for names that are actually current.)
- **Context:** chat = the command's chat, `MessageId = null` (the command message is not stored by
  this point, and the rename is about the user). The audit is therefore written inside `ObserveAsync`.

`MessageBackfillService.BackfillIfMissingAsync` makes the same change for the author of the message
it stores: `ObserveAsync` with `ReplyTarget`, observed now (it has no command date; it runs during
the command's update), `MessageId = null`. In practice the command has already observed the user,
so this is a cheap no-op that guarantees the row before the insert. A message with no `From`
(channel post) stays under user 0 via `ResolveAsync(0)` and relies on the user 0 row (section C).

The non-reply branches (`/ban @username`, id lookups, the inline picker) target a user the app
already has a row for, so they keep `ResolveAsync`.

### C. Migration: repair orphans, then add the key

One EF migration, raw SQL in `Up` (pattern: `20260225233822_ChangeMessageIdFromBigintToInt`,
`20260928003633_AddVerdictEvents`), in this order:

1. **User 0.** Insert a `telegram_users` row for id 0 if absent: not trusted, not active, not banned,
   no names, the non-nullable columns that have no DB default filled the way `GetOrUpdateAsync`'s
   template insert does (`is_trusted`, `bot_dm_enabled`, `first_seen_at`, `last_seen_at`,
   `created_at`, `updated_at`), `names_observed_at` NULL. (My call: matches the row older installs
   already have.)
2. **Orphan users.** For every distinct `messages.user_id` with no `telegram_users` row, insert an
   id-only row the same way. `first_seen_at` / `last_seen_at` = the user's earliest / latest message
   timestamp (the row's history is those messages). `names_observed_at` NULL so the next observation
   records names without fighting a watermark.
3. **Ban status, from the app's own evidence.** Set `is_banned = true`, `banned_at` = the earliest
   qualifying decision's `detected_at`, `ban_expires_at` NULL, for an orphan inserted in step 2 that
   has a `detection_results` row on one of its messages (joined on `(message_id, chat_id)`) where
   `is_spam` and `chat_id <> 0` and either:
   - `source IN (10, 11, 13, 14)` (AutoBan, WebMarkSpam, SpamCommand, ReviewSpam: every one is
     written by `TrainingHandler.CreateSpamSampleAsync`, which `MarkAsSpamAndBanAsync` calls only
     after the global ban succeeded), or
   - `source = 99` (LegacyManual) and `reason = 'Marked as spam by moderator'`: before the verdict
     migration, that reason was written only by `CreateSpamSampleAsync`, called only after a
     successful ban (`ModerationOrchestrator` at `0bf7addb`, `BotModerationService` at `0d19f266`).
     Other LegacyManual rows have no recoverable origin (some are training labels) and do not count.
   Only rows inserted in step 2 are updated; existing users are never touched.
   Accepted edge (my call): an orphan a moderator later unbanned with `/unban` left no trace (the
   unban hit the same missing row), so the repair marks them banned. Nothing recoverable separates
   them from a still-banned orphan; the case needs a ban, a restart gap, and an unban of a user who
   never posted again. From this change on, `/unban` observes its target, so it cannot recur.
4. **Key.** Add `FK_messages_telegram_users_user_id`: `messages(user_id) → telegram_users(telegram_user_id)`,
   `ON DELETE CASCADE` (matches `user_actions.user_id`, `username_history`, `user_tags`: deleting a
   user deletes what belongs to them). Configure the relationship in `AppDbContext` with the Fluent
   API (model first, then `dotnet ef migrations add`, per CLAUDE.md), with the repair SQL added to
   the generated migration before the `AddForeignKey`.

`Down` drops the key. It does not delete repaired rows (they are valid data, and the down migration
cannot tell them from real rows).

The ban rule reads `source` and `reason` (`detection_method` is unreliable: new rows store the enum
name, migrated rows store `Manual`).

### D. Backup restore

`RestoreTableAsync` drops a table's FKs, inserts, then re-adds them validated
(`BackupService.cs:730-760`, re-add around `:848-855`). A backup taken before this change that
contains orphan messages (or user 0 messages on an install without a user 0 row) would fail when
the new key is re-added. Restore order itself is handled: `DependencyResolutionService` reads FKs
from `information_schema` and topologically sorts, so `telegram_users` restores before `messages`.

Add backup format **3.3** with a `Backup32To33...` conversion (pattern:
`Backup31To32TokenTypeMigration`, registered in `ApplyBackupMigrations`, `BackupService.cs:1065-1093`)
that applies the same rules as section C to the backup's `telegram_users`, `messages` and
`detection_results` rows: add user 0 whenever it is absent (as the migration does: restore empties
`telegram_users` first, so a backup without user 0 would otherwise delete the row and break
`AddManualTrainingSampleAsync` under the new key), add id-only rows for orphan message authors, and
set the ban fields by the section C rule. Bump `CurrentVersion`
to `"3.3"`; update the `"3.2"` assertions in `BackupServiceTests` (`:217`, `:684`, `:707`).

The SQL migration and the backup conversion implement one rule twice (different stores, same
reason the two existing conversions mirror their migrations). The plan must keep them to the same
spec, tested with the same cases.

### E. Canonical dataset

Canonical message 211168 (Main Community, `/spam` decision dr 1490) has an author with no
`telegram_users` row: it is the pseudonymised copy of the same orphan shape in the prod snapshot,
copied faithfully. Canonical represents a valid, migrated database, and the new key rejects it at
template load. Remove it (approved): the `19_messages.sql` row (`:1320`) and its `content_hash`
`UPDATE` (`:2412`), detection row 1490 (`32_detection_results.sql:359`), and its
`CanonicalSlices.cs:169` entry. No test or constant references it; the oracle sweep is the only
reader. Update the pinned counts (`LoadCanonicalAsyncTests.cs:32` and
`MigrationTestHelperTemplateTests.cs:36`: messages 410 → 409; `MigrationTestHelperTemplateTests.cs:37`:
detection_results 461 → 460) and the `IntegrationTests/CLAUDE.md` counts table, with a Part 2 note
"(canonical edit 2026-10-19)" saying why the row was removed. Training thresholds are unaffected
(roughly 100 explicit-spam decisions remain against a 20-per-class and 50-spam minimum; the threshold
tests reduce canonical with `KeepSpam`).

Canonical has no message from user 0 (chat-0 samples use @unbeatenmutiny), so the template load
needs no user 0 row; migration-built databases get one anyway.

---

## Testing (test-first)

Every behaviour below gets a test written first and seen failing on current `develop` for the stated
reason before the fix. Canonical anchors follow `.claude/rules/integration-test-data.md`: no seeded
preconditions; a SUT write appears only as the assertion subject; anchors read back first.

### Rename audit (A)

- **Pipeline, new message (integration).** A canonical untrusted user,
  `GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId` (9263051408340
  @pastramiherbs, active, no history), posts a new message in a chat they belong to with different
  names, through `HandleNewMessageAsync` built like production (pattern:
  `MessageProcessingFirstMessageScanTests`). The observation and the message are the subjects.
  Expect: `telegram_users` names updated, one `username_history` row with the old names, one
  `ProfileChange` `user_actions` row with this message's `(message_id, chat_id)`, and the message
  stored. Fails today: the FK error rolls the rename back. Read the user's current names and history
  count back first.
- **Pipeline, service message (integration).** Same user, a service message with text (join) that
  carries new names. Expect the audit to link the stored service message.
- **Edit of an unknown message (unit or integration).** An edit whose message is not stored: the
  rename is recorded and audited with no message link.
- **Audit failure does not undo the rename (unit, `UserIdentityServiceTests`).** `AuditRenameAsync`
  with `IUserActionsRepository.InsertAsync` throwing: logs a warning, does not throw; the repository
  call that recorded the names is unaffected (it no longer writes the audit). Plus a control:
  `GetOrUpdateAsync` writes names + history and no `user_actions` row
  (`TelegramUserRepositoryObserveTests`: replace `Rename_UpdatesNames_WritesOneHistoryAndOneAuditRow`
  with names + history only; the audit assertion moves to the identity-service tests).
- **No-message callers still audit.** An observation with `MessageId == null` and a rename writes
  exactly one `ProfileChange` row with no message link (`JoinRenameRescanTests` already drives this
  path; extend its assertions).

### Reply targets (B)

- **`/spam` on a never-observed user (integration).** The target id must not exist in canonical
  (pick an id outside the canonical user range; the observation creating it is the assertion
  subject, not a precondition). Drive `SpamCommand` with a reply whose `From` carries names, against
  a stubbed Telegram layer. Expect: the user row exists with those names, `is_banned = true`, the
  Ban audit row exists. Fails today with "Cannot set ban status for unknown user".
- **Each reply-target command observes the target (unit).** For each of the seven commands, assert
  `ObserveAsync` is called with the reply's `From` names, `ObservationSource.ReplyTarget` and the
  command message's date, and that `ResolveAsync` is not used for the reply target.
- **`ReplyTarget` records only (unit, `UserIdentityServiceTests`).** A rename observed with
  `ReplyTarget` does not trigger the rescan gate.
- **Backfill observes the author (unit, `MessageBackfillServiceTests`).** Replace the
  `ResolveAsync` stub at `:41`; assert `ObserveAsync(ReplyTarget, MessageId null)` for a message with
  `From`, and user 0 via `ResolveAsync(0)` for one without (`:54` keeps passing).

### Migration (C)

Migration tests use `MigrationTestHelper` (`CreateDatabaseAndMigrateToAsync(previous)` then
`ApplyNextMigrationAsync`), seeding rows at the pre-migration schema as the existing migration tests
do (`AddVerdictEventsMigrationTests`, `StoreVerificationTokenTypeAsIntMigrationTests`); that is the
sanctioned shape for schema fixtures. Cases:

- orphan author with a `SpamCommand` spam decision → row created, banned, `banned_at` = decision time;
- orphan author with a `LegacyManual` row whose reason is `Marked as spam by moderator` → banned;
- orphan author with a `LegacyManual` row with any other reason → row created, not banned;
- orphan author with only a service message → row created, not banned;
- orphan author with a `TrainingDataPage`/`Import` row (chat 0) → not banned;
- an existing user with messages → untouched (names, flags, timestamps);
- no user 0 row (with or without user 0 messages) → user 0 created; an existing user 0 row → untouched;
- after migrating, inserting a message for a nonexistent user fails with the new FK;
- `Down` drops the key and leaves the repaired rows;
- `MigrationWorkflowTests.RollbackSafety_ShouldRevertMostRecentMigration` keeps passing.

### Backup (D)

- `Backup32To33...Tests` (unit, pattern `Backup31To32TokenTypeMigrationTests`): the same cases as
  the migration list, on backup rows.
- `BackupServiceTests`: version assertions updated to `"3.3"`; restoring a 3.2 backup containing an
  orphan message succeeds and produces the repaired row.

### Canonical (E)

- `LoadCanonicalAsyncTests` and `MigrationTestHelperTemplateTests` with the new counts.
- `CanonicalVerdictOracleTests` passes without the removed entry.
- The golden template build succeeds with the new key (this is the check that canonical has no
  other orphan).

## Left to the builder

- Names of `ObservationResult`, `PendingRename`, `AuditRenameAsync`, the backup conversion class,
  and whether `ObservationResult` replaces the `UserIdentity` return or adds an overload. Every
  existing caller must compile against the result.
- Whether `ProfileChangeContext` keeps `MessageId` (it still tells `ObserveAsync` whether to audit
  inline) or the decision becomes an explicit parameter.
- The migration name and exact SQL.
- How the `/spam` integration test stubs the Telegram layer (existing moderation integration tests
  are the pattern).
