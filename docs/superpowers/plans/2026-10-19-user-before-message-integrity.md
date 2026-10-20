# User Before Message Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A user row exists before anything keys off it: renames survive their audit, reply-target moderation records its target, and `messages.user_id` gets a foreign key with existing orphans repaired.

**Architecture:** The rename audit moves out of `TelegramUserRepository.GetOrUpdateAsync` (repositories only map) into `UserIdentityService.ObserveAsync`, written through `IUserActionsRepository` after the names are recorded, with the chat and no message link. Reply-target commands and backfill switch from `ResolveAsync` to `ObserveAsync` with a new records-only `ObservationSource.ReplyTarget`. One EF migration repairs orphans and adds the key; backup format 3.3 applies the same repair to older backups.

**Tech Stack:** .NET 10, EF Core 10 (Npgsql), PostgreSQL 18, NUnit, NSubstitute 6, Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-19-user-before-message-integrity-design.md`

## Global Constraints

- Branch `fix/user-before-message-integrity`; no git worktrees; conventional commits; new commits, never amend. Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- No production details (hosts, real Telegram ids, group names, log excerpts) in code, comments, commits or the PR.
- Integration tests follow `.claude/rules/integration-test-data.md`: no seeded preconditions; a SUT write only as the assertion subject; read anchors back first; migration tests may seed at the pre-migration schema.
- User identities only via `IUserIdentityService` (`.claude/rules/user-identity.md`). Tests build identities with `UserIdentity.ForTest`.
- NSubstitute matcher lambdas: `Arg.Is<T>(x => x!.Prop == y)`, never `?.`.
- EF: model + `AppDbContext` first, then `dotnet ef migrations add`; Fluent API.
- Audit rule: written after the action, only on success; its failure is logged as a warning and never undoes, blocks or precedes the action.
- Ban-derivation sources: `source IN (10, 11, 13, 14)`, or `source = 99 AND reason = 'Marked as spam by moderator'`; always `is_spam AND chat_id <> 0`. `banned_at` = earliest qualifying `detected_at`.
- Test commands: unit `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~<Name>"`; integration (needs Docker) `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~<Name>"`. Targeted runs per task; CI runs everything.

## Review Focus

1. A renamed sender whose message is then auto-banned (the rename scan bans, the pipeline takes the already-banned early return): the rename, its history and its audit must still be recorded. Pinned in Task 2 (the pipeline test's scan returns `Banned`).
2. A reply target that is an anonymous admin or channel (`From` is a system account such as 1087968824, or `From` is null): observing must not break the existing "no sender" handling, and a system account's row stays auto-trusted. Pinned in Task 3 (unit test for `From == null` unchanged; system-id observation uses the repository's existing auto-trust).
3. A sender the app has never seen (the bot itself on a fresh install, or a first-time poster): with the new key, observing must create the row before the message insert. Pinned in Task 5 (`ObserveThenStoreTests`).
4. Restoring a 3.2 backup that has user-0 messages but no user 0 row, and one with no user-0 messages at all: both restore, and user 0 exists afterwards. Pinned in Task 6.
5. Two renames of the same user in one update sequence (rename, then rename back): each writes exactly one history row and one audit row. Pinned in Task 1 (identity-service integration test).

---

### Task 1: The identity service writes the rename audit after the rename

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Models/ProfileChangeContext.cs` (drop `MessageId`)
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/ITelegramUserRepository.cs`, `TelegramUserRepository.cs:66-168`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Identity/UserIdentityService.cs` (constructor + `ObserveAsync`), `IUserIdentityService.cs` (doc only)
- Modify (drop the `MessageId:` argument): `BotProtectionService.cs:94`, `WelcomeService.cs:152`, `BotChatService.cs:541`, `BotMessageService.cs:484`, `ProfileScanService.cs:304`, `MessageProcessingService.cs:217`, `MessageEditProcessor.cs:58`, and every test from `grep -rln "ProfileChangeContext" TelegramGroupsAdmin.*Tests`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Identity/UserIdentityServiceTests.cs`, `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/TelegramUserRepositoryObserveTests.cs`, `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/JoinRenameRescanTests.cs`, create `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/UserIdentityServiceRenameAuditTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ProfileChangeContext(ChatIdentity? Chat);`
  - `ITelegramUserRepository.GetOrUpdateAsync(ObservedUser observed, CancellationToken ct = default) : Task<ObservedNamesResult>` (context parameter removed; writes names, watermark and history only)
  - `UserIdentityService(ITelegramUserRepository users, IProfileScanGate scanGate, IUserActionsRepository actions, ILogger<UserIdentityService> logger)`
  - `ObserveAsync` signature unchanged.

- [ ] **Step 1: Write the failing unit tests** in `UserIdentityServiceTests` (add `_actions = Substitute.For<IUserActionsRepository>()`; pass it to the constructor; update the `Renamed(...)` helper and every `GetOrUpdateAsync` stub to the two-argument signature):

```csharp
[Test]
public async Task Observe_Rename_WritesOneProfileChangeAuditWithChatAndNoMessage()
{
    Renamed(Row(7, "New"));
    IdentityRow(7, "New", NameVerdict.Clean);

    await _sut.ObserveAsync(Observed(7, "New", ObservationSource.ChatMember), new ProfileChangeContext(Chat));

    await _actions.Received(1).InsertAsync(
        Arg.Is<UserActionRecord>(a => a!.UserId == 7 && a.ActionType == UserActionType.ProfileChange
            && a.MessageId == null && a.ChatId == Chat.Id && a.Reason!.Contains("Old")),
        Arg.Any<CancellationToken>());
}

[Test]
public async Task Observe_AuditInsertThrows_StillReturnsIdentity()
{
    Renamed(Row(7, "New"));
    IdentityRow(7, "New", NameVerdict.Clean);
    _actions.InsertAsync(Arg.Any<UserActionRecord>(), Arg.Any<CancellationToken>())
        .Returns<long>(_ => throw new InvalidOperationException("fk"));

    var identity = await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat));

    Assert.That(identity.DisplayName, Is.EqualTo("New"));
}
```

  Also: `Observe_NoRename_WritesNoAudit`, `Observe_RepositoryThrows_WritesNoAudit` (extend the existing `Observe_RepositoryThrows_ReturnsObservedNamesUnscanned`), and `Observe_CancelledWhileAuditing_PropagatesCancellation` (`InsertAsync` throws `OperationCanceledException` with the token cancelled). Keep every existing test.

- [ ] **Step 2: Rewrite the repository test** `Rename_UpdatesNames_WritesOneHistoryAndOneAuditRow` → `Rename_UpdatesNamesAndHistory_WritesNoAuditRow` (anchor `IdentityService.UntrustedNoHistoryUserId`, same read-backs; final assertion: the user's `ProfileChange` count equals `auditBefore`). Drop `NoContext` and the context argument from every call in the file.

- [ ] **Step 3: Write the integration tests**
  - `UserIdentityServiceRenameAuditTests` (golden clone; `AddDataServices` + real `TelegramUserRepository`, `UserActionsRepository`, `UserIdentityService`; substituted `IProfileScanGate`; as `TelegramUserRepositoryObserveTests.SetUp` builds them). Anchor `IdentityService.UntrustedNoHistoryUserId` (9263051408340 @pastramiherbs); read names, history and `ProfileChange` counts back first. `RenameThenRenameBack_WritesTwoHistoryAndTwoAuditRows`: observe "Renamed" at t, then the original first name at t+1s, source `ChatMember`, MainChat context; assert history +2 and `ProfileChange` +2, each with `chat_id` MainChat and `message_id` NULL (Review Focus 5).
  - `JoinRenameRescanTests`: assert its rename wrote exactly one `ProfileChange` row with `message_id` NULL.

- [ ] **Step 4: Run to verify failure** — `dotnet build`; unit `UserIdentityServiceTests`; integration `TelegramUserRepositoryObserveTests|UserIdentityServiceRenameAuditTests|JoinRenameRescanTests`. Expected: FAIL (the repository still writes the audit; the service writes none).

- [ ] **Step 5: Implement**
  - Repository: remove the `UserActions.Add` block (`TelegramUserRepository.cs:134-144`) and the context parameter; keep the history insert and `SaveChangesAsync` when renamed. Interface XML doc: no audit row; the caller audits.
  - Service: after the rescan block in `ObserveAsync`, when `result.Renamed` is not null, write the `ProfileChange` record through `actions.InsertAsync` (`UserActionRecord(Id: 0, UserId: observed.Id, ActionType: ProfileChange, MessageId: null, ChatId: context.Chat?.Id, IssuedBy: Actor.ProfileDiffDetection, IssuedAt: DateTimeOffset.UtcNow, ExpiresAt: null, Reason: AuditReason.WithChatTag(context.Chat, ProfileChangeReason.Build(result.Renamed, observed)))`), inside `try`/`catch (Exception ex) when (!ct.IsCancellationRequested)` logging a warning "Failed to audit rename of user {UserId}; names were recorded".
  - Drop `MessageId:` at every `ProfileChangeContext` construction; drop the context argument from `GetOrUpdateAsync` calls in tests.

- [ ] **Step 6: Run to verify pass** — same filters plus `TelegramUserRepositoryInsertRaceTests|WelcomeService|BotChatServiceIdentityTests|BotProtectionServiceTests|ProfileScanService|MessageProcessingServiceObserveTests|MessageEditProcessorTests`. Expected: PASS.

- [ ] **Step 7: Commit** — `fix(identity): write the rename audit after the names are recorded`

---

### Task 2: A renamed sender's message records the rename (pipeline regression test)

No production code: Task 1 removes the dependency on the message row. This task pins the regression through the real pipeline.

**Files:**
- Test: create `TelegramGroupsAdmin.IntegrationTests/Telegram/MessageProcessingRenameTests.cs`

- [ ] **Step 1: Write the test**, reusing `MessageProcessingFirstMessageScanTests`'s harness (copy its `SetUp`; real `UserIdentityService` with real `UserActionsRepository`; substituted `IProfileScanService` returns `ProfileScanOutcome.Banned`, so detection is short-circuited and the already-banned path runs — Review Focus 1). Anchors: MainChat `-100026957614982`, user `IdentityService.UntrustedNoHistoryUserId` (9263051408340 @pastramiherbs). Read the user's first name, history count and `ProfileChange` count back first.
  - `NewMessage_FromRenamedUser_RecordsRenameAndStoresMessage`: group text message 990101 whose `From` carries the user's id and first name "Renamed Sender". Assert: `telegram_users.first_name == "Renamed Sender"`; history +1 with the old first name; `ProfileChange` +1 with `chat_id` MainChat and `message_id` NULL; message 990101 stored.
  - `ServiceMessage_FromRenamedUser_RecordsRename`: a `NewChatMembers` service message 990102 from the same user as "Renamed Joiner"; same assertions on names, history and audit.

- [ ] **Step 2: Verify it pins the bug** — `git stash` the Task 1 production changes (keep the test), run filter `MessageProcessingRenameTests`: FAIL (names unchanged; history and audit missing). `git stash pop`; rerun: PASS.

- [ ] **Step 3: Commit** — `test(pipeline): a renamed sender's message records the rename`

---

### Task 3: Reply-target commands and backfill observe the target

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Models/ObservationSource.cs` (add `ReplyTarget = 3`)
- Modify: `UserIdentityService.RescansRename` doc comment (lists the records-only sources)
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/{SpamCommand,BanCommand,TempBanCommand,WarnCommand,MuteCommand,TrustCommand,UnbanCommand}.cs` (reply branches only)
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Infrastructure/MessageBackfillService.cs:58-66`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/Commands/ModerationCommandIdentityTests.cs`, `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Infrastructure/MessageBackfillServiceTests.cs`, `UserIdentityServiceTests.cs`, create `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/ReplyTargetObservationTests.cs`

**Interfaces:**
- Produces: `ObservationSource.ReplyTarget = 3` (records only: never rescans).

- [ ] **Step 1: Write the failing tests**
  - `UserIdentityServiceTests.Observe_ReplyTargetRename_RecordsOnly` (mirror `Observe_ChatMemberRename_RecordsOnly`).
  - `ModerationCommandIdentityTests`: stub `_identities.ObserveAsync(Arg.Is<ObservedUser>(o => o!.Id == TargetId), ...)` to return `Target`; drop the `ResolveAsync(TargetId)` stub for reply cases. For each of the seven commands, a test that the reply branch calls `ObserveAsync` with `Arg.Is<ObservedUser>(o => o!.Id == TargetId && o.FirstName == "Stale" && o.Username == "stale_name" && o.Source == ObservationSource.ReplyTarget && o.ObservedAt == ReplyDate)` (give the reply message a fixed `Date`, `ReplyDate`), `ProfileChangeContext` with `Chat.Id == TestChatId`, acts on `Target`, and `DidNotReceive().ResolveAsync(TargetId, ...)`. Rename the class summary: "act on the identity the reply observation returns". The by-id test (`Ban_by_user_id_...`) keeps `ResolveAsync`. Keep each command's existing `From == null` test passing unchanged (Review Focus 2).
  - `MessageBackfillServiceTests`: replace `Backfill_StoresTheAuthorResolvedById` with `Backfill_ObservesTheAuthorAndStoresTheObservedIdentity` (`ObserveAsync` with `ReplyTarget`, `ObservedAt == message.Date`; stored record's `User` is the returned identity). `Backfill_MessageWithoutSender_IsStoredUnderUserZero` stays as is.
  - `ReplyTargetObservationTests` (integration, golden template, real `UserIdentityService` + `TelegramUserRepository` + `UserActionsRepository`): id `4242424243L` (outside the canonical range, as `MessageProcessingFirstMessageScanTests` does; assert absent first). `NeverObservedReplyTarget_IsRecorded_AndCanBeBanned`: `ObserveAsync(new ObservedUser(id, "Reply", "Target", "reply_target", false, ReplyTarget, date), new ProfileChangeContext(MainChat))` then `SetBanStatusAsync(id, isBanned: true)`; assert the row has those names, `is_banned`, `is_trusted == false`. Second case: system id 1087968824 observed as `ReplyTarget` gets `is_trusted == true` (Review Focus 2; assert absent first).

- [ ] **Step 2: Run to verify failure** — unit `ModerationCommandIdentityTests|MessageBackfillServiceTests|UserIdentityServiceTests`, integration `ReplyTargetObservationTests`. Expected: FAIL (`ReplyTarget` undefined; commands call `ResolveAsync`).

- [ ] **Step 3: Implement** — add the enum value; in each command's reply branch build `new ObservedUser(replyFrom.Id, replyFrom.FirstName, replyFrom.LastName, replyFrom.Username, replyFrom.IsBot, ObservationSource.ReplyTarget, message.ReplyToMessage.Date.ToUtcOffset())` with `new ProfileChangeContext(ChatIdentity.From(message.Chat))` and use the returned identity where `ResolveAsync` was used. Backfill: same for `telegramMessage.From` (context chat = `ChatIdentity.FromId(chatId)`); `From == null` keeps `ResolveAsync(0)`. `RescansRename` needs no code change (it already requires `BotUpdate`); update its doc.

- [ ] **Step 4: Run to verify pass** — same filters, plus `CommandReplyMaskingTests|WarnCommandTests|CommandFailureReplyTests`. Expected: PASS.

- [ ] **Step 5: Commit** — `fix(moderation): record the reply target before acting on it`

---

### Task 4: Remove the canonical orphan message

**Files:**
- Modify: `TelegramGroupsAdmin.Testing.Golden/SQL/canonical/19_messages.sql` (row at `:1320`, `UPDATE` at `:2412`), `32_detection_results.sql` (dr 1490 at `:359`)
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/CanonicalSlices.cs:169`
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/LoadCanonicalAsyncTests.cs:32`, `TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelperTemplateTests.cs:36-37`
- Modify: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (counts table: messages 409, explicit_spam slice 99, detection_results 460; Part 2 note "(canonical edit 2026-10-19)")
- Test: create `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/CanonicalMessageAuthorTests.cs`

- [ ] **Step 1: Write the failing test** `CanonicalMessageAuthorTests.EveryCanonicalMessage_HasAnAuthorRow` (golden clone): `SELECT count(*) FROM messages m WHERE NOT EXISTS (SELECT 1 FROM telegram_users u WHERE u.telegram_user_id = m.user_id)` equals 0. Update the pinned counts to 409 / 460.
- [ ] **Step 2: Run to verify failure** — filter `CanonicalMessageAuthorTests|LoadCanonicalAsyncTests|MigrationTestHelperTemplateTests`. Expected: FAIL (1 orphan; counts 410 / 461).
- [ ] **Step 3: Remove** the four data items and the oracle entry; update `IntegrationTests/CLAUDE.md` (counts, and a Part 2 note: message 211168 was the snapshot's one message whose author had no row; removed because the new foreign key makes that state impossible; the migration test covers the shape).
- [ ] **Step 4: Run to verify pass** — same filters plus `CanonicalVerdictOracleTests|GoldenReducePlanTests`. Expected: PASS.
- [ ] **Step 5: Commit** — `test(canonical): remove the message whose author has no user row`

---

### Task 5: Migration repairs orphans and adds the messages → telegram_users key

**Files:**
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs` (messages relationship)
- Create: `TelegramGroupsAdmin.Data/Migrations/<timestamp>_AddMessagesUserForeignKey.cs` (+ Designer, snapshot)
- Test: create `TelegramGroupsAdmin.IntegrationTests/Migrations/AddMessagesUserForeignKeyMigrationTests.cs`, create `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/ObserveThenStoreTests.cs`

**Interfaces:**
- Produces: FK `FK_messages_telegram_users_user_id` (`messages.user_id → telegram_users.telegram_user_id`, `ON DELETE CASCADE`).

- [ ] **Step 1: Write the failing migration tests** (pattern `StoreVerificationTokenTypeAsIntMigrationTests`; `Previous = MigrationId<StoreVerificationTokenTypeAsInt>()`, `Target = MigrationId<AddMessagesUserForeignKey>()`; empty schema, synthetic rows allowed). Seed at `Previous` with `ExecuteSqlAsync` (a managed chat if `messages` needs one, users, messages, `detection_results` with `source`, `classification` 0 for spam, `reason`, `chat_id`). Tests:
  - `Up_OrphanWithSpamCommandDecision_GetsBannedRow` — `banned_at` equals the decision's `detected_at`; names NULL; `is_trusted` false; `first_seen_at`/`last_seen_at` = the orphan's earliest/latest message timestamps.
  - `Up_OrphanWithEachBanningSource_IsBanned` — sources 10, 11, 14 (parameterized).
  - `Up_OrphanWithLegacyModeratorReason_IsBanned` — source 99, reason `Marked as spam by moderator`.
  - `Up_OrphanWithOtherLegacyReason_IsNotBanned` — source 99, reason `Migrated training label`.
  - `Up_OrphanWithOnlyAServiceMessage_IsNotBanned`.
  - `Up_OrphanWithHamDecisionLaterThanNothing_IsNotBanned` — source 13 but `classification` 1 (ham).
  - `Up_ExistingUser_IsUntouched` — a user row with names, `is_banned` false and a source-13 spam decision on their message stays exactly as seeded.
  - `Up_NoUserZero_CreatesIt` and `Up_ExistingUserZero_IsUntouched`.
  - `Up_ThenInsertMessageForUnknownUser_FailsForeignKey` — `PostgresException` with `SqlState == PostgresErrorCodes.ForeignKeyViolation`.
  - `Down_DropsTheKey_KeepsRepairedRows`.
- [ ] **Step 2: Write the failing observe-then-store test** `ObserveThenStoreTests.UnseenSender_ObservedThenStored_Succeeds` (golden clone; real `UserIdentityService`, `TelegramUserRepository`, `UserActionsRepository`, `MessageHistoryRepository`): id `4242424244L` with `IsBot: true` (assert absent first), `ObserveAsync(..., BotUpdate, now)` then `InsertMessageAsync` of a MainChat message from that identity; both writes are the subjects. Assert the message row and the user row exist. This is the order `BotMessageService` uses (`ObserveBotAsync` then insert) and the pipeline uses (Review Focus 3). Control in the same fixture: `InsertMessageAsync` from a second unseen id without observing throws `DbUpdateException` whose inner `PostgresException.SqlState == ForeignKeyViolation` (fails before Step 4: no key yet).
- [ ] **Step 3: Run to verify failure** — filter `AddMessagesUserForeignKeyMigrationTests|ObserveThenStoreTests`. Expected: compile error (no migration), then FAIL.
- [ ] **Step 4: Implement**
  - `AppDbContext`: `modelBuilder.Entity<MessageRecordDto>().HasOne<TelegramUserDto>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);`
  - `dotnet ef migrations add AddMessagesUserForeignKey --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`; in `Up`, insert `migrationBuilder.Sql(...)` before the generated `AddForeignKey`, in order: user 0 (`INSERT ... ON CONFLICT DO NOTHING`), orphan rows (`INSERT ... SELECT user_id, min(timestamp), max(timestamp) ... GROUP BY user_id` filtered on no row), ban fields (`UPDATE telegram_users ... FROM (SELECT ... min(detected_at) ... WHERE <ban rule>)` restricted to ids inserted in this step — capture them in a temp table). `Down`: the generated `DropForeignKey` only.
- [ ] **Step 5: Run to verify pass** — same filters plus `MigrationWorkflowTests` and the Task 4 canonical tests (template build now enforces the key). Expected: PASS.
- [ ] **Step 6: Commit** — `fix(data): require a user row for every message`

---

### Task 6: Backup format 3.3 repairs orphans on restore

**Files:**
- Create: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Migrations/Backup32To33MessageAuthorMigration.cs`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs:45` (`CurrentVersion = "3.3"`), `ApplyBackupMigrations` (`:1065-1093`)
- Test: create `TelegramGroupsAdmin.UnitTests/BackgroundJobs/Services/Backup/Backup32To33MessageAuthorMigrationTests.cs`; modify `TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs` (`:217`, `:684`, `:707` → `"3.3"`)

**Interfaces:**
- Produces: `public static class Backup32To33MessageAuthorMigration { public static void Apply(SystemBackup backup, ILogger logger); }` — reads `telegram_users`, `messages`, `detection_results` rows (snake_case keys, as `Backup31To32TokenTypeMigration` handles them) and appends `telegram_users` rows.

- [ ] **Step 1: Write the failing unit tests** (pattern `Backup31To32TokenTypeMigrationTests`; rows as `JsonElement`s with 3.2 snake_case keys; assert by deserializing appended rows to `TelegramUserDto` with the restore options). The same cases as Task 5 Step 1's `Up_*` list (banning sources, legacy reason, other legacy reason, service-only, ham decision, existing user untouched, user 0 created when absent with and without user-0 messages, existing user 0 untouched), plus `Apply_BackupWithoutMessagesTable_AddsOnlyUserZero` (Review Focus 4).
- [ ] **Step 2: Write the failing integration test** in `BackupServiceTests`: `Restore_32BackupWithOrphanMessage_RestoresAndRepairsTheAuthor` — take a backup of the golden clone, rewrite its metadata version to `"3.2"`, remove one message author's `telegram_users` row and the user 0 row from the backup data (backup data, not the database), restore; assert the author's row exists id-only and user 0 exists. Update the three `"3.2"` assertions.
- [ ] **Step 3: Run to verify failure** — unit `Backup32To33MessageAuthorMigrationTests`, integration `BackupServiceTests`. Expected: FAIL.
- [ ] **Step 4: Implement** the conversion with the Global Constraints ban rule; register it in `ApplyBackupMigrations` behind `BackupFormatVersion.IsOlderThan(backupVersion, "3.3")` with the same "backup-only, time-boxed: remove one year after the release that introduced backup format 3.3" comment; bump `CurrentVersion`. XML doc names the SQL migration it mirrors.
- [ ] **Step 5: Run to verify pass** — same filters plus `BackupFormatVersionTests|Backup31To32TokenTypeMigrationTests`. Expected: PASS.
- [ ] **Step 6: Commit** — `fix(backup): format 3.3 adds missing message authors on restore`

---

### Task 7: Branch verification

- [ ] **Step 1:** `dotnet build` with no new warnings; `dotnet tool run slopwatch` clean.
- [ ] **Step 2:** Unit and integration suites in full (background; integration needs Docker). Expected: all PASS.
- [ ] **Step 3:** `grep -rn "ResolveAsync(.*ReplyToMessage" TelegramGroupsAdmin.Telegram` returns nothing; `grep -rn "MessageId:" TelegramGroupsAdmin.Telegram | grep ProfileChangeContext` returns nothing.
- [ ] **Step 4:** Commit any fixes; then hand off for the whole-branch review.
