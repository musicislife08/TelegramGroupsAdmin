# DM Ban Celebrations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a chat member run `/dmcelebrations on` to receive that chat's ban celebration GIFs as Telegram DMs, independent of whether the chat itself posts them.

**Architecture:** A new `ban_celebration_subscribers` table (DTO → DbSet → domain model → repository) records `(user, chat)` opt-ins. `BanCelebrationSubscriptionService` owns every subscription rule: subscribing, the self-cleaning start prompt, `/start` confirmation, and removal on leave/ban/block. `BanCelebrationService` claims a GIF only when the chat posts it or when there are subscribers who can receive DMs, then hands subscriber delivery to `IUserNotificationService`. That service enqueues to a bounded in-memory channel drained by a single `BackgroundService`, which sends animation DMs through a `NotificationDmDispatcher` shared with the (renamed) `IAdminNotificationService`.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, Telegram.Bot, System.Threading.Channels, NUnit, NSubstitute 6, Testcontainers PostgreSQL (integration tests).

**Spec:** `docs/superpowers/specs/2026-09-25-dm-ban-celebrations-design.md`

## Plan refinements vs. the spec

These came out of reading the code while writing the plan. Each one is reflected in the spec (same commit as this plan).

1. **Leave and block hooks live in `BanCelebrationSubscriptionService`, called from `UpdateRouter`**. The spec put them in `WelcomeService.HandleUserLeftAsync` and `BotChatService`. `WelcomeService`'s leave branch only fires for `Member/Restricted → Left/Kicked`, so an admin leaving would keep their subscription. `UpdateRouter` is dispatch-only, so adding one more dispatch keeps all subscription rules in one service.
2. **Ban removal runs in both ban paths.** `MarkAsSpamAndBanAsync` does not delegate to `BanUserAsync` (it inlines `_banHandler.BanAsync`), and it is the most common auto-ban path. Both paths call one private helper before the celebration step.
3. **Ban ordering is pinned with NSubstitute `Received.InOrder` in `BotModerationServiceTests`.** No integration harness exists for `BanUserAsync`. `ExamFlowServiceTests` already uses `Received.InOrder` for ordering invariants.
4. **No new backup guard test.** `BackupServiceTests` already asserts that the discovered table count equals the live `public` table count (minus `__EFMigrationsHistory`, `cached_blocked_domains`, `file_scan_quota`). A new table with a mis-named DTO already fails that test. We bump `ExpectedBackupTableCount` 43 → 44 and add a direct discovery-mapping test.
5. **No `queueOnBlock` flag.** Animation DMs never queue on 403 (the same rule as keyboard DMs), and admin payloads never carry animations. Stale `file_id` retry lives inside `BotDmService`.
6. **The banned-user celebration DM goes through `IUserNotificationService.SendBanCelebrationToBannedUserAsync`.** The dispatcher is `internal` to the web project, and `BanCelebrationService` lives in the Telegram project.
7. **The notification implementation classes become `internal sealed`.** `NotificationPayload` is `internal`, and a public constructor taking an internal type does not compile (CS0051). MS DI constructs internal types with public constructors.
8. **Removal of dead code**: `BanCelebrationService._mediaBasePath` and its `IOptions<AppOptions>` dependency have had no reader since GIF paths moved to `IBanCelebrationGifRepository.GetFullPath`.

## Global Constraints

- Branch: `feat/dm-ban-celebrations` (exists, off `develop`, holds the spec commits). Never commit to `develop` or `master`. PR targets `develop`.
- Conventional commits. Use `git commit -F- <<'EOF'` heredocs. Every message ends with a blank line followed by `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.
- Only repositories touch `AppDbContext`. Commands, services, handlers, and workers use repository interfaces and domain models.
- Schema via Fluent API in `AppDbContext` first, then `dotnet ef migrations add <Name> -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`.
- The DTO must be `TelegramGroupsAdmin.Data.Models.BanCelebrationSubscriberDto` with `[Table("ban_celebration_subscribers")]`. The backup discovery reflection depends on all three.
- Integration tests use canonical data only (`.claude/rules/integration-test-data.md`). The **one approved exception** is `36_ban_celebration_subscribers.sql` (4 rows, approved 2026-09-25). A SUT write appears in a test only when it is the assertion subject.
- NSubstitute matcher lambdas: `Arg.Is<T>(x => x!.Prop == y)`. Use `!`, never `?.`.
- One type per file. No tuples across method boundaries. No `[Obsolete]`, no backward-compat shims. Remove dead code you orphan.
- Do not create or edit any `appsettings*.json`.
- Command name is exactly `dmcelebrations`. The deep-link payload is exactly `dmcel_{chatId}`. The prompt lifetime is exactly 60 seconds. Command replies auto-delete after 30 seconds.
- Fan-out channel capacity is 100 with `BoundedChannelFullMode.DropOldest`, and there is a single reader. Pacing between subscriber sends is 50 ms.
- Integration tests need Docker. Run only the filters each task gives until Task 11.

## Review Focus

These input classes are the most likely to bite a real user, and no task would otherwise test them. Each has a test in the task named.

1. **A subscriber who is themselves the banned user, via the spam auto-ban path.** Expect no celebration DM about their own ban. Covered by the Task 9 `MarkAsSpamAndBanAsync_RemovesDmCelebrationSubscriptionsBeforeCelebrating` test.
2. **A chat admin (status `Administrator`) leaving the group.** Expect their subscription for that chat to be removed. Covered by the Task 5 `HandleChatMemberUpdateAsync_AdministratorLeaves_RemovesThatChat` test.
3. **A malformed or foreign `/start` payload** (`dmcel_abc`, `dmcel_`, a chat the sender never subscribed to). Expect a neutral reply, no deletions, no errors. Covered by Task 5 `DmCelebrationDeepLinkTests` and Task 8 `StartCommand_DmCelebrationsPayload_NotSubscribed_RepliesNeutrally`.
4. **`/dmcelebrations ON`, or extra words after the argument.** Expect the command to be case-insensitive on the first argument and to ignore the rest. Covered by the Task 8 `Execute_UppercaseOnWithTrailingWords_Subscribes` test.
5. **A chat whose name is unknown (`ChatName` null) at fan-out time.** Expect the DM header to fall back to the chat id, not an empty bold line. Covered by the Task 7 `SendAsync_ChatWithoutName_UsesChatIdAsHeader` test.

---

### Task 1: Rename `INotificationService` → `IAdminNotificationService`

**Files:**
- Rename: `TelegramGroupsAdmin.Core/Services/INotificationService.cs` → `IAdminNotificationService.cs`
- Rename: `TelegramGroupsAdmin/Services/NotificationService.cs` → `AdminNotificationService.cs`
- Rename: `TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationServiceRoutingTests.cs` → `AdminNotificationServiceRoutingTests.cs`
- Modify: every `.cs` / `.razor` that references either name (about 20 files, all mechanical)

**Interfaces:**
- Consumes: nothing.
- Produces: `TelegramGroupsAdmin.Core.Services.IAdminNotificationService` (same members as today's `INotificationService`), implemented by `TelegramGroupsAdmin.Services.AdminNotificationService`. Every later task uses these names.

- [ ] **Step 1: Move the files**

```bash
git mv TelegramGroupsAdmin.Core/Services/INotificationService.cs TelegramGroupsAdmin.Core/Services/IAdminNotificationService.cs
git mv TelegramGroupsAdmin/Services/NotificationService.cs TelegramGroupsAdmin/Services/AdminNotificationService.cs
git mv TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationServiceRoutingTests.cs TelegramGroupsAdmin.UnitTests/Services/Notifications/AdminNotificationServiceRoutingTests.cs
```

- [ ] **Step 2: Rewrite identifiers**

`perl` is used because BSD `sed` has no `\b`. The look-behind stops `WebPushNotificationService`, `_mockNotificationService`, and similar names from matching.

```bash
git ls-files '*.cs' '*.razor' | xargs perl -pi -e 's/\bINotificationService\b/IAdminNotificationService/g; s/\bNotificationServiceRoutingTests\b/AdminNotificationServiceRoutingTests/g; s/(?<![A-Za-z_])NotificationService\b/AdminNotificationService/g'
```

- [ ] **Step 3: Update the interface doc comment**

In `TelegramGroupsAdmin.Core/Services/IAdminNotificationService.cs`, make the `<summary>` on the interface read:

```csharp
/// <summary>
/// Admin-facing notifications: routed to web users with chat access and unlinked Telegram
/// chat admins, filtered by each web user's per-event channel preferences (DM, email, web push).
/// User-facing, opt-in notifications live on <see cref="IUserNotificationService"/>.
/// </summary>
```

`IUserNotificationService` does not exist until Task 7. Write the `<see cref>` as plain text `IUserNotificationService` for now, and Task 7 Step 1 turns it into a `<see cref>`.

- [ ] **Step 4: Verify nothing still uses the old names**

Run: `git grep -nE '\bINotificationService\b|(^|[^A-Za-z_])NotificationService\b' -- '*.cs' '*.razor'`
Expected: no output.

- [ ] **Step 5: Build and run the affected unit tests**

Run: `dotnet build TelegramGroupsAdmin.sln` → Expected: `Build succeeded` with 0 errors.
Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~AdminNotificationServiceRoutingTests|FullyQualifiedName~BotModerationServiceTests|FullyQualifiedName~NotificationHandlerTests|FullyQualifiedName~ExamFlowServiceTests|FullyQualifiedName~ChatHealthRefreshOrchestratorTests"` → Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor: rename INotificationService to IAdminNotificationService

Makes room for a user-facing notification interface. Mechanical rename of
the interface, implementation and routing tests; no behaviour change.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 2: Schema, migration, canonical rows, and backup accounting

**Files:**
- Create: `TelegramGroupsAdmin.Data/Models/BanCelebrationSubscriberDto.cs`
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs` (DbSet near line 70; composite key near line 110; relationship in `ConfigureRelationships`; index in `ConfigureIndexes`)
- Create: migration `AddBanCelebrationSubscribers` (generated)
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/36_ban_celebration_subscribers.sql`
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDatasetConstants.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`
- Test: `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/LoadCanonicalAsyncTests.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Services/Backup/TableDiscoveryServiceTests.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs`

**Interfaces:**
- Consumes: `TelegramUserDto` (`[Key] TelegramUserId`), `ManagedChatRecordDto` (`[Key] ChatId`).
- Produces: `AppDbContext.BanCelebrationSubscribers` (`DbSet<BanCelebrationSubscriberDto>`), with properties `TelegramUserId` (long), `ChatId` (long), `SubscribedAt` (DateTimeOffset), `PromptMessageId` (int?), `PromptDeleteJobId` (string?), and navigations `TelegramUser`, `ManagedChat`. Also produces `GoldenDatasetConstants.DmCelebrations.*` (below). Task 3 uses both.

- [ ] **Step 1: Write the failing tests**

In `LoadCanonicalAsyncTests.cs`, rename `LoadCanonicalAsync_PopulatesAllThirtyFiveTables` to `LoadCanonicalAsync_PopulatesAllThirtySixTables` and add this assertion after the `WelcomeResponses` line:

```csharp
        Assert.That(await ctx.BanCelebrationSubscribers.CountAsync(), Is.EqualTo(4),
            "ban_celebration_subscribers should be exactly 4 (approved canonical addition 2026-09-25)");
```

In `TableDiscoveryServiceTests.cs`, add:

```csharp
    [Test]
    public async Task DiscoverTablesAsync_IncludesBanCelebrationSubscribers()
    {
        using var testHelper = new MigrationTestHelper();
        await testHelper.CreateDatabaseFromGoldenTemplateAsync();

        await using var connection = new NpgsqlConnection(testHelper.ConnectionString);
        await connection.OpenAsync();

        var service = new TableDiscoveryService(Substitute.For<ILogger<TableDiscoveryService>>());

        var mapping = await service.DiscoverTablesAsync(connection);

        Assert.That(mapping.ContainsKey("ban_celebration_subscribers"), Is.True,
            $"Expected ban_celebration_subscribers in mapping. Actual keys: {string.Join(", ", mapping.Keys.OrderBy(k => k))}");
        Assert.That(mapping["ban_celebration_subscribers"].Name, Is.EqualTo("BanCelebrationSubscriberDto"));
    }
```

In `BackupServiceTests.cs`, change `ExpectedBackupTableCount` from `43` to `44` and append to the comment above it: `Updated 2026-09-25: +ban_celebration_subscribers (DM ban celebrations).`

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~LoadCanonicalAsync_PopulatesAllThirtySixTables|FullyQualifiedName~DiscoverTablesAsync_IncludesBanCelebrationSubscribers"`
Expected: FAIL. The build breaks because `AppDbContext` has no `BanCelebrationSubscribers`.

- [ ] **Step 3: Create the DTO**

`TelegramGroupsAdmin.Data/Models/BanCelebrationSubscriberDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for ban_celebration_subscribers table.
/// One row per (Telegram user, chat) opt-in to receive that chat's ban celebrations by DM.
/// Deliverability is not stored here — it is the join to telegram_users.bot_dm_enabled.
/// </summary>
[Table("ban_celebration_subscribers")]
public class BanCelebrationSubscriberDto
{
    [Column("telegram_user_id")]
    public long TelegramUserId { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("subscribed_at")]
    public DateTimeOffset SubscribedAt { get; set; }

    /// <summary>
    /// Message id of the open "tap to start the bot" prompt in the chat, or null when none is open.
    /// </summary>
    [Column("prompt_message_id")]
    public int? PromptMessageId { get; set; }

    /// <summary>
    /// Quartz job id of the scheduled 60-second prompt deletion, or null when none is pending.
    /// </summary>
    [Column("prompt_delete_job_id")]
    [MaxLength(200)]
    public string? PromptDeleteJobId { get; set; }

    public TelegramUserDto? TelegramUser { get; set; }

    public ManagedChatRecordDto? ManagedChat { get; set; }
}
```

- [ ] **Step 4: Configure `AppDbContext`**

Add the DbSet under `// Ban celebration tables`:

```csharp
    public DbSet<BanCelebrationSubscriberDto> BanCelebrationSubscribers => Set<BanCelebrationSubscriberDto>();
```

Add the composite key next to the other composite keys in `OnModelCreating` (after the `TrainingLabelDto` key):

```csharp
        modelBuilder.Entity<BanCelebrationSubscriberDto>().HasKey(s => new { s.TelegramUserId, s.ChatId });
```

In `ConfigureRelationships`, add:

```csharp
        // BanCelebrationSubscribers → TelegramUsers / ManagedChats (cascade: a subscription
        // cannot outlive the user or the chat it refers to)
        modelBuilder.Entity<BanCelebrationSubscriberDto>()
            .HasOne(s => s.TelegramUser)
            .WithMany()
            .HasForeignKey(s => s.TelegramUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BanCelebrationSubscriberDto>()
            .HasOne(s => s.ManagedChat)
            .WithMany()
            .HasForeignKey(s => s.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
```

In `ConfigureIndexes`, add this. The PK leads with the user, and fan-out queries by chat:

```csharp
        // BanCelebrationSubscribers index — fan-out reads subscribers by chat
        modelBuilder.Entity<BanCelebrationSubscriberDto>()
            .HasIndex(s => s.ChatId);
```

- [ ] **Step 5: Generate and inspect the migration**

Run: `dotnet ef migrations add AddBanCelebrationSubscribers -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`
Expected: a new `*_AddBanCelebrationSubscribers.cs` that creates `ban_celebration_subscribers` with PK `PK_ban_celebration_subscribers (telegram_user_id, chat_id)`, two FKs with `onDelete: ReferentialAction.Cascade`, and index `IX_ban_celebration_subscribers_chat_id`. Nothing else should change. If the migration touches any other table, stop and investigate.

- [ ] **Step 6: Add the canonical rows**

`TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/36_ban_celebration_subscribers.sql`. Each line is one statement, in the same `--column-inserts` shape as the other files:

```sql
INSERT INTO ban_celebration_subscribers (telegram_user_id, chat_id, subscribed_at, prompt_message_id, prompt_delete_job_id) VALUES (9183753414221, -100059667856554, '2026-09-20 18:04:11+00', NULL, NULL);
INSERT INTO ban_celebration_subscribers (telegram_user_id, chat_id, subscribed_at, prompt_message_id, prompt_delete_job_id) VALUES (9011393194616, -100059667856554, '2026-09-21 02:13:40+00', 424242, 'canonical-stale-prompt-job');
INSERT INTO ban_celebration_subscribers (telegram_user_id, chat_id, subscribed_at, prompt_message_id, prompt_delete_job_id) VALUES (9689750659830, -100059667856554, '2026-09-22 15:30:02+00', NULL, NULL);
INSERT INTO ban_celebration_subscribers (telegram_user_id, chat_id, subscribed_at, prompt_message_id, prompt_delete_job_id) VALUES (9689750659830, -100017608907459, '2026-09-22 15:31:47+00', NULL, NULL);
```

In `GoldenDataset.cs`, append after `"SQL.canonical.35_message_translations.sql",`:

```csharp
            // Layer 1 (late addition) — child of telegram_users + managed_chats
            "SQL.canonical.36_ban_celebration_subscribers.sql", // 4 rows (approved addition 2026-09-25)
```

Update the two "35" mentions in that file's comments (`Loads the 35 canonical/*.sql fixtures`, `(35 files;`) to 36.

- [ ] **Step 7: Pin the anchors**

In `GoldenDatasetConstants.cs`, add a nested class after `UsersPage`:

```csharp
    /// <summary>
    /// DM ban celebration subscriber anchors from <c>canonical/36_ban_celebration_subscribers.sql</c>
    /// (canonical addition 2026-09-25 — a new table has no row to flag-edit; approved by owner).
    /// Every user is active, not banned, not a bot, and has real messages in the chats they are
    /// subscribed to.
    /// </summary>
    public static class DmCelebrations
    {
        /// <summary>Workshop Alumni — hosts three of the four subscriber rows.</summary>
        public const long WorkshopAlumniChatId = -100059667856554L;

        /// <summary>Poultry Community — second chat for the two-chat subscriber.</summary>
        public const long PoultryCommunityChatId = -100017608907459L;

        /// <summary>@magnetismvoucher — bot_dm_enabled=true; subscribed to Workshop Alumni. The deliverable subscriber.</summary>
        public const long DeliverableSubscriberId = 9183753414221L;

        /// <summary>@thudupper — bot_dm_enabled=false; subscribed to Workshop Alumni with a stale open prompt (never started the bot, prompt timed out).</summary>
        public const long UndeliverableSubscriberId = 9011393194616L;

        /// <summary>@deepnessunmapped — bot_dm_enabled=false; subscribed to Workshop Alumni and Poultry Community.</summary>
        public const long TwoChatSubscriberId = 9689750659830L;

        /// <summary>@chummyrepair — bot_dm_enabled=true; MainChat member with no subscription row.</summary>
        public const long UnsubscribedMemberId = 9306234060091L;

        /// <summary>Stale prompt message id on <see cref="UndeliverableSubscriberId"/>'s Workshop Alumni row.</summary>
        public const int StalePromptMessageId = 424242;

        /// <summary>Stale prompt delete-job id on <see cref="UndeliverableSubscriberId"/>'s Workshop Alumni row.</summary>
        public const string StalePromptJobId = "canonical-stale-prompt-job";
    }
```

- [ ] **Step 8: Document the canonical addition**

In `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`, add a row to the "Tables and counts" table after row 35:

```markdown
| 36 | ban_celebration_subscribers | 4 | Approved canonical addition 2026-09-25 (new table — no row to flag-edit). See Part 2 "DM ban celebration subscribers". |
```

Under `## Part 2 - Scenario recipes`, before `### Synthetic / reserved rows`, add:

```markdown
### DM ban celebration subscribers (canonical addition 2026-09-25)

Anchors are in code as `GoldenDatasetConstants.DmCelebrations`. None of these users was referenced by any test or doc before this addition.

| User | Id | `bot_dm_enabled` | Rows | Use when |
|---|---|---|---|---|
| @magnetismvoucher | `9183753414221` | true | Workshop Alumni | a subscriber who is deliverable |
| @thudupper | `9011393194616` | false | Workshop Alumni, stale prompt `424242` / `canonical-stale-prompt-job` | a subscriber who is not deliverable; cleanup of a timed-out prompt |
| @deepnessunmapped | `9689750659830` | false | Workshop Alumni + Poultry Community | removing one chat must leave the other |
| @chummyrepair | `9306234060091` | true | none (MainChat member) | the subscribe path, where the SUT upsert is the assertion subject |

Workshop Alumni (`-100059667856554`) has no `ban_celebration_config`, so its effective celebration config is disabled: a subscribers-only chat.
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~LoadCanonicalAsyncTests|FullyQualifiedName~TableDiscoveryServiceTests|FullyQualifiedName~BackupServiceTests"`
Expected: all pass. This includes the existing live-count test (`metadata.TableCount == actualTableCount`), which proves the DTO naming contract.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(data): add ban_celebration_subscribers table and canonical anchors

Composite-key table (telegram_user_id, chat_id) with cascade FKs to
telegram_users and managed_chats, plus the prompt bookkeeping columns.
Adds the approved 4-row canonical fixture and bumps the backup table count.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 3: Subscriber domain model, mapping, and repository

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Models/BanCelebrationSubscriber.cs`
- Create: `TelegramGroupsAdmin.Telegram/Repositories/Mappings/BanCelebrationSubscriberMappings.cs`
- Create: `TelegramGroupsAdmin.Telegram/Repositories/IBanCelebrationSubscriberRepository.cs`
- Create: `TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationSubscriberRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs` (next to line 55, `IBanCelebrationGifRepository`)
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationSubscriberRepositoryTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.BanCelebrationSubscribers`, `GoldenDatasetConstants.DmCelebrations.*` (Task 2); `UserIdentity.From(TelegramUserDto)` (existing extension in `TelegramGroupsAdmin.Telegram.Extensions`).
- Produces:
  - `record BanCelebrationSubscriber(long TelegramUserId, long ChatId, DateTimeOffset SubscribedAt, int? PromptMessageId, string? PromptDeleteJobId)`
  - `IBanCelebrationSubscriberRepository`:
    - `Task<bool> UpsertAsync(long telegramUserId, long chatId, CancellationToken ct = default)` returns true when a row was created
    - `Task<BanCelebrationSubscriber?> GetAsync(long telegramUserId, long chatId, CancellationToken ct = default)`
    - `Task<bool> DeleteAsync(long telegramUserId, long chatId, CancellationToken ct = default)`
    - `Task<int> DeleteAllForUserAsync(long telegramUserId, CancellationToken ct = default)`
    - `Task<bool> HasDeliverableSubscribersAsync(long chatId, CancellationToken ct = default)`
    - `Task<List<UserIdentity>> GetDeliverableSubscribersAsync(long chatId, CancellationToken ct = default)` ordered by `SubscribedAt`
    - `Task SetPromptAsync(long telegramUserId, long chatId, int promptMessageId, string promptDeleteJobId, CancellationToken ct = default)`
    - `Task ClearPromptAsync(long telegramUserId, long chatId, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing integration tests**

`TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationSubscriberRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using Anchors = TelegramGroupsAdmin.IntegrationTests.TestData.GoldenDatasetConstants.DmCelebrations;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for <see cref="BanCelebrationSubscriberRepository"/> against the canonical
/// dataset. Anchors: <c>canonical/36_ban_celebration_subscribers.sql</c> (approved canonical
/// addition 2026-09-25). SetUp re-reads every anchor's shape so a canonical change fails loudly.
/// </summary>
[TestFixture]
public class BanCelebrationSubscriberRepositoryTests
{
    private const long MainChatId = GoldenDatasetConstants.Chats.MainChatId;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IBanCelebrationSubscriberRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IBanCelebrationSubscriberRepository, BanCelebrationSubscriberRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IBanCelebrationSubscriberRepository>();

        await AssertCanonicalAnchorsAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task AssertCanonicalAnchorsAsync()
    {
        await using var ctx = _testHelper!.GetDbContext();

        var rows = await ctx.BanCelebrationSubscribers.AsNoTracking()
            .Select(s => new { s.TelegramUserId, s.ChatId })
            .ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(4), "canonical ban_celebration_subscribers changed");

        var dm = await ctx.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == Anchors.DeliverableSubscriberId
                        || u.TelegramUserId == Anchors.UndeliverableSubscriberId
                        || u.TelegramUserId == Anchors.TwoChatSubscriberId
                        || u.TelegramUserId == Anchors.UnsubscribedMemberId)
            .ToDictionaryAsync(u => u.TelegramUserId, u => u.BotDmEnabled);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dm[Anchors.DeliverableSubscriberId], Is.True);
            Assert.That(dm[Anchors.UndeliverableSubscriberId], Is.False);
            Assert.That(dm[Anchors.TwoChatSubscriberId], Is.False);
            Assert.That(dm[Anchors.UnsubscribedMemberId], Is.True);
        }
    }

    [Test]
    public async Task HasDeliverableSubscribersAsync_ChatWithDmEnabledSubscriber_ReturnsTrue()
    {
        Assert.That(await _repository.HasDeliverableSubscribersAsync(Anchors.WorkshopAlumniChatId), Is.True);
    }

    [Test]
    public async Task HasDeliverableSubscribersAsync_ChatWithOnlyDmDisabledSubscribers_ReturnsFalse()
    {
        Assert.That(await _repository.HasDeliverableSubscribersAsync(Anchors.PoultryCommunityChatId), Is.False);
    }

    [Test]
    public async Task HasDeliverableSubscribersAsync_ChatWithNoSubscribers_ReturnsFalse()
    {
        Assert.That(await _repository.HasDeliverableSubscribersAsync(MainChatId), Is.False);
    }

    [Test]
    public async Task GetDeliverableSubscribersAsync_ReturnsOnlyDmEnabledSubscribersWithIdentity()
    {
        var subscribers = await _repository.GetDeliverableSubscribersAsync(Anchors.WorkshopAlumniChatId);

        Assert.That(subscribers, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subscribers[0].Id, Is.EqualTo(Anchors.DeliverableSubscriberId));
            Assert.That(subscribers[0].Username, Is.EqualTo("magnetismvoucher"));
        }
    }

    [Test]
    public async Task UpsertAsync_NewSubscription_CreatesRowAndReturnsTrue()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        var created = await _repository.UpsertAsync(Anchors.UnsubscribedMemberId, MainChatId);

        var row = await _repository.GetAsync(Anchors.UnsubscribedMemberId, MainChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.True);
            Assert.That(row, Is.Not.Null);
            Assert.That(row!.SubscribedAt, Is.GreaterThan(before));
            Assert.That(row.PromptMessageId, Is.Null);
            Assert.That(row.PromptDeleteJobId, Is.Null);
        }
    }

    [Test]
    public async Task UpsertAsync_ExistingSubscription_ReturnsFalseAndLeavesRowUntouched()
    {
        var created = await _repository.UpsertAsync(Anchors.UndeliverableSubscriberId, Anchors.WorkshopAlumniChatId);

        var row = await _repository.GetAsync(Anchors.UndeliverableSubscriberId, Anchors.WorkshopAlumniChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.False);
            Assert.That(row!.PromptMessageId, Is.EqualTo(Anchors.StalePromptMessageId));
            Assert.That(row.PromptDeleteJobId, Is.EqualTo(Anchors.StalePromptJobId));
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesOnlyThatChatsRow()
    {
        var deleted = await _repository.DeleteAsync(Anchors.TwoChatSubscriberId, Anchors.PoultryCommunityChatId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(await _repository.GetAsync(Anchors.TwoChatSubscriberId, Anchors.PoultryCommunityChatId), Is.Null);
            Assert.That(await _repository.GetAsync(Anchors.TwoChatSubscriberId, Anchors.WorkshopAlumniChatId), Is.Not.Null);
        }
    }

    [Test]
    public async Task DeleteAsync_NoRow_ReturnsFalse()
    {
        Assert.That(await _repository.DeleteAsync(Anchors.UnsubscribedMemberId, MainChatId), Is.False);
    }

    [Test]
    public async Task DeleteAllForUserAsync_RemovesEveryChatAndReturnsCount()
    {
        var removed = await _repository.DeleteAllForUserAsync(Anchors.TwoChatSubscriberId);

        await using var ctx = _testHelper!.GetDbContext();
        var remaining = await ctx.BanCelebrationSubscribers.CountAsync(s => s.TelegramUserId == Anchors.TwoChatSubscriberId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(remaining, Is.Zero);
        }
    }

    [Test]
    public async Task SetPromptAsync_StoresMessageAndJobIds()
    {
        await _repository.SetPromptAsync(Anchors.DeliverableSubscriberId, Anchors.WorkshopAlumniChatId, 5150, "job-5150");

        var row = await _repository.GetAsync(Anchors.DeliverableSubscriberId, Anchors.WorkshopAlumniChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row!.PromptMessageId, Is.EqualTo(5150));
            Assert.That(row.PromptDeleteJobId, Is.EqualTo("job-5150"));
        }
    }

    [Test]
    public async Task ClearPromptAsync_NullsBothPromptColumns()
    {
        await _repository.ClearPromptAsync(Anchors.UndeliverableSubscriberId, Anchors.WorkshopAlumniChatId);

        var row = await _repository.GetAsync(Anchors.UndeliverableSubscriberId, Anchors.WorkshopAlumniChatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row!.PromptMessageId, Is.Null);
            Assert.That(row.PromptDeleteJobId, Is.Null);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~BanCelebrationSubscriberRepositoryTests"`
Expected: FAIL. The build breaks because `IBanCelebrationSubscriberRepository` does not exist.

- [ ] **Step 3: Create the model and mapping**

`TelegramGroupsAdmin.Telegram/Models/BanCelebrationSubscriber.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for one (user, chat) DM ban celebration opt-in, including any open start prompt.
/// </summary>
public sealed record BanCelebrationSubscriber(
    long TelegramUserId,
    long ChatId,
    DateTimeOffset SubscribedAt,
    int? PromptMessageId,
    string? PromptDeleteJobId);
```

`TelegramGroupsAdmin.Telegram/Repositories/Mappings/BanCelebrationSubscriberMappings.cs`:

```csharp
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for DM ban celebration subscriber records.
/// </summary>
public static class BanCelebrationSubscriberMappings
{
    extension(DataModels.BanCelebrationSubscriberDto data)
    {
        public UiModels.BanCelebrationSubscriber ToModel() => new(
            data.TelegramUserId,
            data.ChatId,
            data.SubscribedAt,
            data.PromptMessageId,
            data.PromptDeleteJobId);
    }
}
```

Only `ToModel` is needed, because writes go through repository methods that build the DTO themselves. Do not add an unused `ToDto`.

- [ ] **Step 4: Create the interface and repository**

`TelegramGroupsAdmin.Telegram/Repositories/IBanCelebrationSubscriberRepository.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for DM ban celebration opt-ins. The only component that touches the
/// ban_celebration_subscribers table. "Deliverable" always means the subscriber's
/// telegram_users.bot_dm_enabled is true — the join lives here, never in callers.
/// </summary>
public interface IBanCelebrationSubscriberRepository
{
    /// <summary>Creates the (user, chat) row if absent. Returns true when a row was created.</summary>
    Task<bool> UpsertAsync(long telegramUserId, long chatId, CancellationToken ct = default);

    Task<BanCelebrationSubscriber?> GetAsync(long telegramUserId, long chatId, CancellationToken ct = default);

    /// <summary>Deletes the (user, chat) row. Returns true when a row existed.</summary>
    Task<bool> DeleteAsync(long telegramUserId, long chatId, CancellationToken ct = default);

    /// <summary>Deletes every row for the user. Returns the number of rows removed.</summary>
    Task<int> DeleteAllForUserAsync(long telegramUserId, CancellationToken ct = default);

    /// <summary>True when the chat has at least one subscriber with bot DMs enabled.</summary>
    Task<bool> HasDeliverableSubscribersAsync(long chatId, CancellationToken ct = default);

    /// <summary>Subscribers of the chat with bot DMs enabled, oldest subscription first.</summary>
    Task<List<UserIdentity>> GetDeliverableSubscribersAsync(long chatId, CancellationToken ct = default);

    Task SetPromptAsync(long telegramUserId, long chatId, int promptMessageId, string promptDeleteJobId, CancellationToken ct = default);

    Task ClearPromptAsync(long telegramUserId, long chatId, CancellationToken ct = default);
}
```

`TelegramGroupsAdmin.Telegram/Repositories/BanCelebrationSubscriberRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <inheritdoc />
public sealed class BanCelebrationSubscriberRepository(
    IDbContextFactory<AppDbContext> contextFactory) : IBanCelebrationSubscriberRepository
{
    public async Task<bool> UpsertAsync(long telegramUserId, long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        if (await context.BanCelebrationSubscribers.AnyAsync(
                s => s.TelegramUserId == telegramUserId && s.ChatId == chatId, ct))
        {
            return false;
        }

        context.BanCelebrationSubscribers.Add(new BanCelebrationSubscriberDto
        {
            TelegramUserId = telegramUserId,
            ChatId = chatId,
            SubscribedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent /dmcelebrations on inserted the same row between AnyAsync and SaveChanges.
            return false;
        }
    }

    public async Task<BanCelebrationSubscriber?> GetAsync(long telegramUserId, long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dto = await context.BanCelebrationSubscribers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TelegramUserId == telegramUserId && s.ChatId == chatId, ct);
        return dto?.ToModel();
    }

    public async Task<bool> DeleteAsync(long telegramUserId, long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var deleted = await context.BanCelebrationSubscribers
            .Where(s => s.TelegramUserId == telegramUserId && s.ChatId == chatId)
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public async Task<int> DeleteAllForUserAsync(long telegramUserId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.BanCelebrationSubscribers
            .Where(s => s.TelegramUserId == telegramUserId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<bool> HasDeliverableSubscribersAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.BanCelebrationSubscribers
            .AnyAsync(s => s.ChatId == chatId && s.TelegramUser!.BotDmEnabled, ct);
    }

    public async Task<List<UserIdentity>> GetDeliverableSubscribersAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var users = await context.BanCelebrationSubscribers.AsNoTracking()
            .Where(s => s.ChatId == chatId && s.TelegramUser!.BotDmEnabled)
            .OrderBy(s => s.SubscribedAt)
            .Select(s => s.TelegramUser!)
            .ToListAsync(ct);
        return users.Select(UserIdentity.From).ToList();
    }

    public async Task SetPromptAsync(long telegramUserId, long chatId, int promptMessageId, string promptDeleteJobId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.BanCelebrationSubscribers
            .Where(s => s.TelegramUserId == telegramUserId && s.ChatId == chatId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.PromptMessageId, promptMessageId)
                .SetProperty(s => s.PromptDeleteJobId, promptDeleteJobId), ct);
    }

    public async Task ClearPromptAsync(long telegramUserId, long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.BanCelebrationSubscribers
            .Where(s => s.TelegramUserId == telegramUserId && s.ChatId == chatId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.PromptMessageId, (int?)null)
                .SetProperty(s => s.PromptDeleteJobId, (string?)null), ct);
    }
}
```

If `UserIdentity.From` does not bind as a method group, because it is a C# 14 static extension member, use `.Select(u => UserIdentity.From(u))`.

- [ ] **Step 5: Register the repository**

In `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`, directly under the `IBanCelebrationGifRepository` line:

```csharp
            services.AddScoped<IBanCelebrationSubscriberRepository, BanCelebrationSubscriberRepository>(); // DM ban celebration opt-ins
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~BanCelebrationSubscriberRepositoryTests"`
Expected: 11 tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(telegram): add ban celebration subscriber repository

Domain model, mapping and repository for DM celebration opt-ins. The
bot_dm_enabled deliverability join lives in the repository. Integration
tests run against the canonical subscriber anchors.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 4: Animation DM transport, file-id error helper, and metrics

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Helpers/TelegramFileIdErrors.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs` (replace private `IsInvalidFileIdError` with the helper)
- Modify: `TelegramGroupsAdmin.Telegram/Services/DmDeliveryResult.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/IBotDmService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotDmService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Helpers/TelegramFileIdErrorsTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Bot/BotDmServiceTests.cs`

**Interfaces:**
- Consumes: `IBotMessageHandler.SendAnimationAsync(long chatId, InputFile animation, string? caption = null, ParseMode? parseMode = null, ReplyParameters? replyParameters = null, InlineKeyboardMarkup? replyMarkup = null, IReadOnlyList<MessageEntity>? captionEntities = null, CancellationToken ct = default)`.
- Produces:
  - `static class TelegramFileIdErrors { static bool IsInvalidFileId(Exception ex) }`
  - `DmDeliveryResult.Blocked` (bool) and `DmDeliveryResult.AnimationFileId` (string?)
  - `IBotDmService.SendDmWithAnimationEntitiesAsync(UserIdentity user, TelegramMessage caption, string? fileId, string? filePath, CancellationToken cancellationToken = default)`. This never queues. On 403 it sets `Blocked = true` and calls `DisableBotDmAsync`. A rejected cached `fileId` is retried as an upload from `filePath`. `AnimationFileId` is the `file_id` Telegram returned.
  - `PipelineMetrics.RecordBanCelebrationDm(string outcome)` and `PipelineMetrics.RecordBanCelebrationSubscription(string action, long count = 1)`

- [ ] **Step 1: Write the failing tests**

`TelegramGroupsAdmin.UnitTests/Telegram/Helpers/TelegramFileIdErrorsTests.cs`:

```csharp
using Telegram.Bot.Exceptions;
using TelegramGroupsAdmin.Telegram.Helpers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Helpers;

[TestFixture]
public class TelegramFileIdErrorsTests
{
    [TestCase("Bad Request: wrong file identifier/HTTP URL specified")]
    [TestCase("Bad Request: invalid file_id")]
    [TestCase("Bad Request: INVALID FILE")]
    public void IsInvalidFileId_FileIdErrors_ReturnsTrue(string message)
    {
        Assert.That(TelegramFileIdErrors.IsInvalidFileId(new ApiRequestException(message, 400)), Is.True);
    }

    [TestCase("Forbidden: bot was blocked by the user")]
    [TestCase("Bad Request: chat not found")]
    public void IsInvalidFileId_OtherErrors_ReturnsFalse(string message)
    {
        Assert.That(TelegramFileIdErrors.IsInvalidFileId(new ApiRequestException(message, 400)), Is.False);
    }
}
```

Append this region to `BotDmServiceTests.cs` (the existing `SetUp` already builds `_service`, `_messageHandler`, `_userRepository`, `_pendingNotificationsRepository`; add `using Telegram.Bot.Exceptions;`):

```csharp
    #region SendDmWithAnimationEntitiesAsync

    private void SetupAnimationReturns(string returnedFileId) =>
        _messageHandler
            .SendAnimationAsync(
                Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<string?>(), Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<IReadOnlyList<MessageEntity>?>(), Arg.Any<CancellationToken>())
            .Returns(new Message
            {
                Id = 7,
                Chat = new Chat { Id = TestUser.Id },
                Animation = new Animation { FileId = returnedFileId, FileUniqueId = "u1" }
            });

    [Test]
    public async Task SendDmWithAnimationEntitiesAsync_CachedFileId_SendsByFileIdAndReportsReturnedId()
    {
        SetupAnimationReturns("cached-id");
        var caption = new TelegramMessageBuilder().Bold("Workshop Alumni").LineBreak().Text("banned!").Build();

        var result = await _service.SendDmWithAnimationEntitiesAsync(TestUser, caption, "cached-id", "/nope.gif");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.True);
            Assert.That(result.Blocked, Is.False);
            Assert.That(result.AnimationFileId, Is.EqualTo("cached-id"));
        }
        await _messageHandler.Received(1).SendAnimationAsync(
            TestUser.Id,
            Arg.Is<InputFile>(f => f is InputFileId && ((InputFileId)f).Id == "cached-id"),
            caption.Text, Arg.Any<ParseMode?>(), Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
            caption.Entities, Arg.Any<CancellationToken>());
        await _userRepository.Received(1).EnableBotDmAsync(TestUser.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendDmWithAnimationEntitiesAsync_Forbidden_ReportsBlockedDisablesDmAndDoesNotQueue()
    {
        _messageHandler
            .SendAnimationAsync(
                Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<string?>(), Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<IReadOnlyList<MessageEntity>?>(), Arg.Any<CancellationToken>())
            .Returns<Message>(_ => throw new ApiRequestException("Forbidden: bot was blocked by the user", 403));

        var result = await _service.SendDmWithAnimationEntitiesAsync(TestUser, TelegramMessage.Plain("x"), "cached-id", null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.False);
            Assert.That(result.Blocked, Is.True);
            Assert.That(result.Failed, Is.True);
        }
        await _userRepository.Received(1).DisableBotDmAsync(TestUser.Id, Arg.Any<CancellationToken>());
        await _pendingNotificationsRepository.DidNotReceiveWithAnyArgs().AddPendingNotificationAsync(default, default!, default!, default);
    }

    [Test]
    public async Task SendDmWithAnimationEntitiesAsync_StaleFileId_RetriesWithUploadAndReportsNewId()
    {
        var path = Path.Combine(Path.GetTempPath(), $"anim_{Guid.NewGuid():N}.gif");
        await File.WriteAllBytesAsync(path, [0x47, 0x49, 0x46]);
        try
        {
            _messageHandler
                .SendAnimationAsync(
                    Arg.Any<long>(), Arg.Is<InputFile>(f => f is InputFileId), Arg.Any<string?>(), Arg.Any<ParseMode?>(),
                    Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                    Arg.Any<IReadOnlyList<MessageEntity>?>(), Arg.Any<CancellationToken>())
                .Returns<Message>(_ => throw new ApiRequestException("Bad Request: wrong file identifier/HTTP URL specified", 400));
            _messageHandler
                .SendAnimationAsync(
                    Arg.Any<long>(), Arg.Is<InputFile>(f => f is InputFileStream), Arg.Any<string?>(), Arg.Any<ParseMode?>(),
                    Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                    Arg.Any<IReadOnlyList<MessageEntity>?>(), Arg.Any<CancellationToken>())
                .Returns(new Message
                {
                    Id = 8,
                    Chat = new Chat { Id = TestUser.Id },
                    Animation = new Animation { FileId = "fresh-id", FileUniqueId = "u2" }
                });

            var result = await _service.SendDmWithAnimationEntitiesAsync(TestUser, TelegramMessage.Plain("x"), "stale-id", path);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.DmSent, Is.True);
                Assert.That(result.AnimationFileId, Is.EqualTo("fresh-id"));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task SendDmWithAnimationEntitiesAsync_NoFileIdAndMissingFile_FailsWithoutCallingTelegram()
    {
        var result = await _service.SendDmWithAnimationEntitiesAsync(TestUser, TelegramMessage.Plain("x"), null, "/does/not/exist.gif");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.False);
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Blocked, Is.False);
        }
        await _messageHandler.DidNotReceiveWithAnyArgs().SendAnimationAsync(default, default!);
    }

    #endregion
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~TelegramFileIdErrorsTests|FullyQualifiedName~BotDmServiceTests"`
Expected: FAIL. The build breaks because `TelegramFileIdErrors`, `SendDmWithAnimationEntitiesAsync`, and `Blocked` do not exist.

- [ ] **Step 3: Add the helper and switch `BanCelebrationService` to it**

`TelegramGroupsAdmin.Telegram/Helpers/TelegramFileIdErrors.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Helpers;

/// <summary>
/// Recognises Telegram errors meaning a cached file_id is no longer usable, so callers
/// can clear the cache and fall back to uploading from disk.
/// </summary>
public static class TelegramFileIdErrors
{
    public static bool IsInvalidFileId(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("wrong file identifier") ||
               message.Contains("file_id") ||
               message.Contains("invalid file");
    }
}
```

In `BanCelebrationService.cs`, delete the private static `IsInvalidFileIdError` method and its doc comment. Change `catch (Exception ex) when (IsInvalidFileIdError(ex))` to `catch (Exception ex) when (TelegramFileIdErrors.IsInvalidFileId(ex))`, and add `using TelegramGroupsAdmin.Telegram.Helpers;`.

- [ ] **Step 4: Extend `DmDeliveryResult`**

Append to `DmDeliveryResult`:

```csharp
    /// <summary>
    /// True when the recipient has blocked the bot (Telegram 403). Only set by delivery
    /// paths that do not queue, so callers can act on the block (e.g. drop opt-in subscriptions).
    /// </summary>
    public bool Blocked { get; init; }

    /// <summary>
    /// The animation file_id Telegram returned for a sent animation, for callers that cache it.
    /// </summary>
    public string? AnimationFileId { get; init; }
```

- [ ] **Step 5: Add the DM method**

In `IBotDmService.cs`, append:

```csharp
    /// <summary>
    /// Send an animation (GIF) DM with an entity-based caption. Uses <paramref name="fileId"/>
    /// when given, falling back to uploading <paramref name="filePath"/> if Telegram rejects it.
    /// Does NOT queue on failure: a 403 returns <see cref="DmDeliveryResult.Blocked"/> and
    /// disables bot DMs for the user. <see cref="DmDeliveryResult.AnimationFileId"/> carries the
    /// file_id Telegram returned so callers can cache it.
    /// </summary>
    Task<DmDeliveryResult> SendDmWithAnimationEntitiesAsync(
        UserIdentity user,
        TelegramMessage caption,
        string? fileId,
        string? filePath,
        CancellationToken cancellationToken = default);
```

In `BotDmService.cs`, add `using TelegramGroupsAdmin.Telegram.Helpers;` and these members (next to `SendDmWithKeyboardAsync`, which uses the same no-queue pattern):

```csharp
    /// <inheritdoc />
    public async Task<DmDeliveryResult> SendDmWithAnimationEntitiesAsync(
        UserIdentity user,
        TelegramMessage caption,
        string? fileId,
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sent = await SendAnimationWithFallbackAsync(user, caption, fileId, filePath, cancellationToken);
            if (sent is null)
            {
                logger.LogWarning(
                    "Animation DM to {User} skipped: no cached file_id and file not found at {Path}",
                    user.ToLogDebug(), filePath);
                return new DmDeliveryResult
                {
                    DmSent = false,
                    Failed = true,
                    ErrorMessage = "No cached file_id and the animation file was not found on disk"
                };
            }

            await telegramUserRepository.EnableBotDmAsync(user.Id, cancellationToken);
            logger.LogDebug("Animation DM sent to {User} (MessageId: {MessageId})", user.ToLogDebug(), sent.MessageId);

            return new DmDeliveryResult
            {
                DmSent = true,
                MessageId = sent.MessageId,
                AnimationFileId = sent.Animation?.FileId
            };
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            logger.LogInformation("{User} has blocked bot DMs (403) - animation not queued", user.ToLogInfo());
            await telegramUserRepository.DisableBotDmAsync(user.Id, cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = false,
                Failed = true,
                Blocked = true,
                ErrorMessage = "User has blocked bot DMs - animations are not queued"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send animation DM to {User}", user.ToLogDebug());
            return new DmDeliveryResult { DmSent = false, Failed = true, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Sends by cached file_id when possible; on an invalid-file_id error, uploads from disk.
    /// Returns null when there is nothing to send (no usable file_id and no file on disk).
    /// A 403 or any other error propagates to the caller's handling.
    /// </summary>
    private async Task<Message?> SendAnimationWithFallbackAsync(
        UserIdentity user,
        TelegramMessage caption,
        string? fileId,
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(fileId))
        {
            try
            {
                return await messageHandler.SendAnimationAsync(
                    chatId: user.Id,
                    animation: InputFile.FromFileId(fileId),
                    caption: caption.Text,
                    captionEntities: caption.Entities,
                    ct: cancellationToken);
            }
            catch (Exception ex) when (TelegramFileIdErrors.IsInvalidFileId(ex))
            {
                logger.LogWarning(
                    "Cached animation file_id rejected for DM to {User}; uploading from disk",
                    user.ToLogDebug());
            }
        }

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await messageHandler.SendAnimationAsync(
            chatId: user.Id,
            animation: InputFile.FromStream(stream, Path.GetFileName(filePath)),
            caption: caption.Text,
            captionEntities: caption.Entities,
            ct: cancellationToken);
    }
```

- [ ] **Step 6: Add the metrics**

In `PipelineMetrics.cs`, add two fields:

```csharp
    private readonly Counter<long> _banCelebrationDmTotal;
    private readonly Counter<long> _banCelebrationSubscriptionTotal;
```

Initialise them in the constructor after `_banCelebrationMaskedUsernameTotal`:

```csharp
        _banCelebrationDmTotal = _meter.CreateCounter<long>(
            "tga.pipeline.ban_celebration.dm_total",
            description: "DM ban celebration deliveries by outcome (sent, blocked, failed, dropped)");
        _banCelebrationSubscriptionTotal = _meter.CreateCounter<long>(
            "tga.pipeline.ban_celebration.subscription_total",
            description: "DM ban celebration subscription changes by action (subscribe, unsubscribe, left, banned, blocked)");
```

Add two methods after `RecordMaskedUsername`:

```csharp
    public void RecordBanCelebrationDm(string outcome)
    {
        _banCelebrationDmTotal.Add(1, new TagList { { "outcome", outcome } });
    }

    public void RecordBanCelebrationSubscription(string action, long count = 1)
    {
        _banCelebrationSubscriptionTotal.Add(count, new TagList { { "action", action } });
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~TelegramFileIdErrorsTests|FullyQualifiedName~BotDmServiceTests|FullyQualifiedName~BanCelebrationServiceTests"`
Expected: all pass. The existing `BanCelebrationServiceTests` prove the helper swap changed nothing.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(telegram): add non-queuing animation DM transport and celebration metrics

SendDmWithAnimationEntitiesAsync reuses a cached file_id, retries stale
ones as uploads, reports the returned file_id, and surfaces a 403 as
Blocked instead of queueing. The file_id error check is shared with the
chat celebration path.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 5: `BanCelebrationSubscriptionService` and the deep link

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Services/DmCelebrations/DmCelebrationDeepLink.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/DmCelebrations/DmCelebrationSubscribeResult.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/DmCelebrations/SubscriptionRemovalReason.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/DmCelebrations/IBanCelebrationSubscriptionService.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/DmCelebrations/BanCelebrationSubscriptionService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/DmCelebrations/DmCelebrationDeepLinkTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/DmCelebrations/BanCelebrationSubscriptionServiceTests.cs`

**Interfaces:**
- Consumes: `IBanCelebrationSubscriberRepository` (Task 3); `PipelineMetrics.RecordBanCelebrationSubscription` (Task 4); `ITelegramUserRepository.GetByTelegramIdAsync` / `DisableBotDmAsync`; `IManagedChatsRepository` via `ChatIdentity.FromAsync(long, IManagedChatsRepository, CancellationToken)`; `IBotMessageService.SendAndSaveMessageAsync(long chatId, TelegramMessage message, ReplyParameters? replyParameters = null, InlineKeyboardMarkup? replyMarkup = null, CancellationToken cancellationToken = default)` and `DeleteAndMarkMessageAsync(long chatId, int messageId, string deletionSource, CancellationToken)` (the latter already swallows "message not found"); `IBotUserService.GetMeAsync`; `IJobScheduler.ScheduleJobAsync<TPayload>(string jobName, TPayload payload, int delaySeconds, string? deduplicationKey, CancellationToken)` / `CancelJobAsync(string jobId, CancellationToken)`; `DeleteMessagePayload(long ChatId, int MessageId, string Reason)`; job name `"DeleteMessage"`.
- Produces (namespace `TelegramGroupsAdmin.Telegram.Services.DmCelebrations`):
  - `static class DmCelebrationDeepLink { string Build(string botUsername, long chatId); bool TryParseChatId(string? payload, out long chatId); }`
  - `enum DmCelebrationSubscribeResult { Subscribed, AwaitingStart }`
  - `enum SubscriptionRemovalReason { Banned, Blocked }`
  - `IBanCelebrationSubscriptionService`:
    - `Task<DmCelebrationSubscribeResult> SubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default)`
    - `Task<bool> UnsubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default)`
    - `Task<bool> IsSubscribedAsync(long chatId, long userId, CancellationToken ct = default)`
    - `Task<ChatIdentity?> ConfirmFromStartAsync(long chatId, UserIdentity user, CancellationToken ct = default)` returns null when the user has no subscription for that chat
    - `Task HandleChatMemberUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default)`
    - `Task HandleBotMembershipUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default)`
    - `Task RemoveAllForUserAsync(UserIdentity user, SubscriptionRemovalReason reason, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing deep-link tests**

`TelegramGroupsAdmin.UnitTests/Telegram/Services/DmCelebrations/DmCelebrationDeepLinkTests.cs`:

```csharp
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.DmCelebrations;

[TestFixture]
public class DmCelebrationDeepLinkTests
{
    [Test]
    public void Build_ProducesStartLinkWithChatIdPayload()
    {
        Assert.That(DmCelebrationDeepLink.Build("tga_bot", -100059667856554L),
            Is.EqualTo("https://t.me/tga_bot?start=dmcel_-100059667856554"));
    }

    [Test]
    public void TryParseChatId_RoundTripsNegativeChatId()
    {
        var ok = DmCelebrationDeepLink.TryParseChatId("dmcel_-100059667856554", out var chatId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.True);
            Assert.That(chatId, Is.EqualTo(-100059667856554L));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("dmcel_")]
    [TestCase("dmcel_abc")]
    [TestCase("dmcel_-100_5")]
    [TestCase("welcome_-100059667856554_42")]
    [TestCase("DMCEL_-100059667856554")]
    public void TryParseChatId_RejectsForeignOrMalformedPayloads(string? payload)
    {
        Assert.That(DmCelebrationDeepLink.TryParseChatId(payload, out _), Is.False);
    }
}
```

- [ ] **Step 2: Write the failing service tests**

`TelegramGroupsAdmin.UnitTests/Telegram/Services/DmCelebrations/BanCelebrationSubscriptionServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.DmCelebrations;

[TestFixture]
public class BanCelebrationSubscriptionServiceTests
{
    private const long ChatId = -100059667856554L;
    private const long UserId = 42L;
    private static readonly ChatIdentity Chat = new(ChatId, "Workshop Alumni");
    private static readonly UserIdentity User = new(UserId, "Kim", null, "kim");

    private IBanCelebrationSubscriberRepository _repository = null!;
    private ITelegramUserRepository _telegramUsers = null!;
    private IManagedChatsRepository _managedChats = null!;
    private IBotMessageService _messages = null!;
    private IBotUserService _botUser = null!;
    private IJobScheduler _jobs = null!;
    private BanCelebrationSubscriptionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IBanCelebrationSubscriberRepository>();
        _telegramUsers = Substitute.For<ITelegramUserRepository>();
        _managedChats = Substitute.For<IManagedChatsRepository>();
        _messages = Substitute.For<IBotMessageService>();
        _botUser = Substitute.For<IBotUserService>();
        _jobs = Substitute.For<IJobScheduler>();

        _botUser.GetMeAsync(Arg.Any<CancellationToken>()).Returns(new User { Id = 1, IsBot = true, FirstName = "Bot", Username = "tga_bot" });
        _messages.SendAndSaveMessageAsync(Arg.Any<long>(), Arg.Any<TelegramMessage>(), Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(), Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 777, Chat = new Chat { Id = ChatId } });
        _jobs.ScheduleJobAsync(Arg.Any<string>(), Arg.Any<DeleteMessagePayload>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("job-777");

        _sut = new BanCelebrationSubscriptionService(
            _repository, _telegramUsers, _managedChats, _messages, _botUser, _jobs,
            new PipelineMetrics(), NullLogger<BanCelebrationSubscriptionService>.Instance);
    }

    private void DmEnabled(bool enabled) =>
        _telegramUsers.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new TelegramUser(
                TelegramUserId: UserId, Username: "kim", FirstName: "Kim", LastName: null,
                UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
                IsBot: false, IsTrusted: false, IsBanned: false, KickCount: 0, BotDmEnabled: enabled,
                FirstSeenAt: DateTimeOffset.UtcNow, LastSeenAt: DateTimeOffset.UtcNow,
                CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow));

    private static ManagedChatRecord ManagedChat(long chatId, string name) => new(
        Identity: new ChatIdentity(chatId, name), ChatType: ManagedChatType.Supergroup,
        BotStatus: BotChatStatus.Administrator, IsAdmin: true, AddedAt: DateTimeOffset.UtcNow,
        IsActive: true, IsDeleted: false, LastSeenAt: null, SettingsJson: null, ChatIconPath: null);

    private static BanCelebrationSubscriber Row(int? promptId = null, string? jobId = null) =>
        new(UserId, ChatId, DateTimeOffset.UtcNow, promptId, jobId);

    [Test]
    public async Task SubscribeAsync_DmEnabled_UpsertsAndReturnsSubscribedWithoutPrompt()
    {
        DmEnabled(true);

        var result = await _sut.SubscribeAsync(Chat, User);

        Assert.That(result, Is.EqualTo(DmCelebrationSubscribeResult.Subscribed));
        await _repository.Received(1).UpsertAsync(UserId, ChatId, Arg.Any<CancellationToken>());
        await _messages.DidNotReceiveWithAnyArgs().SendAndSaveMessageAsync(default, default(TelegramMessage)!);
    }

    [Test]
    public async Task SubscribeAsync_DmDisabled_PostsDeepLinkPromptAndSchedulesSixtySecondDelete()
    {
        DmEnabled(false);
        InlineKeyboardMarkup? keyboard = null;
        TelegramMessage? prompt = null;
        _messages.SendAndSaveMessageAsync(ChatId, Arg.Do<TelegramMessage>(m => prompt = m), Arg.Any<ReplyParameters?>(),
                Arg.Do<InlineKeyboardMarkup?>(k => keyboard = k), Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 777, Chat = new Chat { Id = ChatId } });

        var result = await _sut.SubscribeAsync(Chat, User);

        Assert.That(result, Is.EqualTo(DmCelebrationSubscribeResult.AwaitingStart));
        var button = keyboard!.InlineKeyboard.Single().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(button.Url, Is.EqualTo("https://t.me/tga_bot?start=dmcel_-100059667856554"));
            Assert.That(prompt!.Entities.Any(e => e.Type == MessageEntityType.TextMention && e.User!.Id == UserId), Is.True);
            Assert.That(prompt.Text, Does.Contain("Workshop Alumni"));
        }
        await _jobs.Received(1).ScheduleJobAsync(
            "DeleteMessage",
            Arg.Is<DeleteMessagePayload>(p => p!.ChatId == ChatId && p.MessageId == 777),
            60, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).SetPromptAsync(UserId, ChatId, 777, "job-777", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SubscribeAsync_DmDisabledWithOpenPrompt_CleansOldPromptBeforePostingNew()
    {
        DmEnabled(false);
        _repository.GetAsync(UserId, ChatId, Arg.Any<CancellationToken>()).Returns(Row(500, "job-500"));

        await _sut.SubscribeAsync(Chat, User);

        Received.InOrder(() =>
        {
            _jobs.CancelJobAsync("job-500", Arg.Any<CancellationToken>());
            _messages.DeleteAndMarkMessageAsync(ChatId, 500, Arg.Any<string>(), Arg.Any<CancellationToken>());
            _repository.ClearPromptAsync(UserId, ChatId, Arg.Any<CancellationToken>());
            _messages.SendAndSaveMessageAsync(ChatId, Arg.Any<TelegramMessage>(), Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task UnsubscribeAsync_ExistingRowWithPrompt_CleansPromptAndDeletes()
    {
        _repository.GetAsync(UserId, ChatId, Arg.Any<CancellationToken>()).Returns(Row(500, "job-500"));

        var removed = await _sut.UnsubscribeAsync(Chat, User);

        Assert.That(removed, Is.True);
        await _jobs.Received(1).CancelJobAsync("job-500", Arg.Any<CancellationToken>());
        await _messages.Received(1).DeleteAndMarkMessageAsync(ChatId, 500, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnsubscribeAsync_NoRow_ReturnsFalseAndDeletesNothing()
    {
        var removed = await _sut.UnsubscribeAsync(Chat, User);

        Assert.That(removed, Is.False);
        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Test]
    public async Task ConfirmFromStartAsync_NoRow_ReturnsNullAndTouchesNothing()
    {
        var chat = await _sut.ConfirmFromStartAsync(ChatId, User);

        Assert.That(chat, Is.Null);
        await _jobs.DidNotReceiveWithAnyArgs().CancelJobAsync(default!);
        await _messages.DidNotReceiveWithAnyArgs().DeleteAndMarkMessageAsync(default, default);
    }

    [Test]
    public async Task ConfirmFromStartAsync_RowWithPrompt_CancelsJobDeletesPromptClearsAndReturnsChat()
    {
        _repository.GetAsync(UserId, ChatId, Arg.Any<CancellationToken>()).Returns(Row(500, "job-500"));
        _managedChats.GetByChatIdAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(ManagedChat(ChatId, "Workshop Alumni"));

        var chat = await _sut.ConfirmFromStartAsync(ChatId, User);

        Assert.That(chat!.ChatName, Is.EqualTo("Workshop Alumni"));
        await _jobs.Received(1).CancelJobAsync("job-500", Arg.Any<CancellationToken>());
        await _messages.Received(1).DeleteAndMarkMessageAsync(ChatId, 500, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).ClearPromptAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    private static ChatMemberUpdated MemberUpdate(ChatType chatType, ChatMember oldMember, ChatMember newMember) => new()
    {
        Chat = new Chat { Id = chatType == ChatType.Private ? UserId : ChatId, Type = chatType, Title = "Workshop Alumni" },
        From = new User { Id = UserId, FirstName = "Kim" },
        Date = DateTime.UtcNow,
        OldChatMember = oldMember,
        NewChatMember = newMember
    };

    private static User TgUser => new() { Id = UserId, FirstName = "Kim" };

    [Test]
    public async Task HandleChatMemberUpdateAsync_MemberLeaves_RemovesThatChat()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberMember { User = TgUser }, new ChatMemberLeft { User = TgUser }));

        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdateAsync_MemberKicked_RemovesThatChat()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberMember { User = TgUser }, new ChatMemberBanned { User = TgUser }));

        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdateAsync_AdministratorLeaves_RemovesThatChat()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberAdministrator { User = TgUser }, new ChatMemberLeft { User = TgUser }));

        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdateAsync_Join_DoesNothing()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberLeft { User = TgUser }, new ChatMemberMember { User = TgUser }));

        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_PrivateChatBlocked_DisablesDmAndRemovesAll()
    {
        _repository.DeleteAllForUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(2);

        await _sut.HandleBotMembershipUpdateAsync(MemberUpdate(ChatType.Private,
            new ChatMemberMember { User = TgUser }, new ChatMemberBanned { User = TgUser }));

        await _telegramUsers.Received(1).DisableBotDmAsync(UserId, Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteAllForUserAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_PrivateChatUnblocked_DoesNothing()
    {
        await _sut.HandleBotMembershipUpdateAsync(MemberUpdate(ChatType.Private,
            new ChatMemberBanned { User = TgUser }, new ChatMemberMember { User = TgUser }));

        await _repository.DidNotReceiveWithAnyArgs().DeleteAllForUserAsync(default);
        await _telegramUsers.DidNotReceiveWithAnyArgs().DisableBotDmAsync(default);
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_GroupChat_DoesNothing()
    {
        await _sut.HandleBotMembershipUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberMember { User = TgUser }, new ChatMemberBanned { User = TgUser }));

        await _repository.DidNotReceiveWithAnyArgs().DeleteAllForUserAsync(default);
    }

    [Test]
    public async Task RemoveAllForUserAsync_DeletesEveryRowForTheUser()
    {
        await _sut.RemoveAllForUserAsync(User, SubscriptionRemovalReason.Banned);

        await _repository.Received(1).DeleteAllForUserAsync(UserId, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~DmCelebrations"`
Expected: FAIL. The build breaks because the `DmCelebrations` namespace does not exist.

- [ ] **Step 4: Implement the deep link and enums**

`DmCelebrationDeepLink.cs`:

```csharp
using System.Globalization;

namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <summary>
/// Builds and parses the /start deep link used by the DM ban celebration start prompt.
/// The payload carries only the chat id — never a message id — so a crafted link can only
/// confirm the sender's own existing subscription, never choose what the bot deletes.
/// </summary>
public static class DmCelebrationDeepLink
{
    private const string Prefix = "dmcel_";

    public static string Build(string botUsername, long chatId) =>
        $"https://t.me/{botUsername}?start={Prefix}{chatId.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParseChatId(string? payload, out long chatId)
    {
        chatId = 0;
        return payload is not null
               && payload.StartsWith(Prefix, StringComparison.Ordinal)
               && long.TryParse(payload.AsSpan(Prefix.Length), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out chatId);
    }
}
```

`DmCelebrationSubscribeResult.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <summary>Outcome of <c>/dmcelebrations on</c>.</summary>
public enum DmCelebrationSubscribeResult
{
    /// <summary>The user can already receive bot DMs; celebrations start with the next ban.</summary>
    Subscribed,

    /// <summary>The subscription is saved and a start prompt was posted; DMs begin once the user starts the bot.</summary>
    AwaitingStart
}
```

`SubscriptionRemovalReason.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <summary>Why every one of a user's DM celebration subscriptions was removed at once.</summary>
public enum SubscriptionRemovalReason
{
    Banned,
    Blocked
}
```

- [ ] **Step 5: Implement the service**

`IBanCelebrationSubscriptionService.cs`:

```csharp
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <summary>
/// Owns every DM ban celebration subscription rule: opting in and out, the self-cleaning
/// start prompt, /start confirmation, and removal when a user leaves, is banned, or blocks the bot.
/// </summary>
public interface IBanCelebrationSubscriptionService
{
    Task<DmCelebrationSubscribeResult> SubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default);

    /// <summary>Returns true when a subscription existed and was removed.</summary>
    Task<bool> UnsubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default);

    Task<bool> IsSubscribedAsync(long chatId, long userId, CancellationToken ct = default);

    /// <summary>
    /// Handles /start dmcel_{chatId}: closes the open start prompt for the sender's own subscription.
    /// Returns the chat, or null when the sender has no subscription for that chat.
    /// </summary>
    Task<ChatIdentity?> ConfirmFromStartAsync(long chatId, UserIdentity user, CancellationToken ct = default);

    /// <summary>ChatMember updates in groups: leaving or being kicked removes that chat's subscription.</summary>
    Task HandleChatMemberUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default);

    /// <summary>MyChatMember updates in private chats: blocking the bot removes every subscription.</summary>
    Task HandleBotMembershipUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default);

    Task RemoveAllForUserAsync(UserIdentity user, SubscriptionRemovalReason reason, CancellationToken ct = default);
}
```

`BanCelebrationSubscriptionService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <inheritdoc />
public sealed class BanCelebrationSubscriptionService(
    IBanCelebrationSubscriberRepository subscriberRepository,
    ITelegramUserRepository telegramUserRepository,
    IManagedChatsRepository managedChatsRepository,
    IBotMessageService messageService,
    IBotUserService userService,
    IJobScheduler jobScheduler,
    PipelineMetrics pipelineMetrics,
    ILogger<BanCelebrationSubscriptionService> logger) : IBanCelebrationSubscriptionService
{
    internal const int PromptLifetimeSeconds = 60;
    private const string DeleteMessageJobName = "DeleteMessage";
    private const string PromptTimeoutReason = "dmcelebrations_prompt_timeout";
    private const string PromptCleanupSource = "dmcelebrations_prompt";

    public async Task<DmCelebrationSubscribeResult> SubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default)
    {
        if (await subscriberRepository.UpsertAsync(user.Id, chat.Id, ct))
        {
            pipelineMetrics.RecordBanCelebrationSubscription("subscribe");
            logger.LogInformation("{User} subscribed to DM ban celebrations from {Chat}", user.ToLogInfo(), chat.ToLogInfo());
        }

        var telegramUser = await telegramUserRepository.GetByTelegramIdAsync(user.Id, ct);
        if (telegramUser?.BotDmEnabled == true)
        {
            return DmCelebrationSubscribeResult.Subscribed;
        }

        await PostStartPromptAsync(chat, user, ct);
        return DmCelebrationSubscribeResult.AwaitingStart;
    }

    public async Task<bool> UnsubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default)
    {
        var row = await subscriberRepository.GetAsync(user.Id, chat.Id, ct);
        if (row is null)
        {
            return false;
        }

        await CleanupPromptAsync(row, ct);
        await subscriberRepository.DeleteAsync(user.Id, chat.Id, ct);
        pipelineMetrics.RecordBanCelebrationSubscription("unsubscribe");
        logger.LogInformation("{User} unsubscribed from DM ban celebrations in {Chat}", user.ToLogInfo(), chat.ToLogInfo());
        return true;
    }

    public async Task<bool> IsSubscribedAsync(long chatId, long userId, CancellationToken ct = default) =>
        await subscriberRepository.GetAsync(userId, chatId, ct) is not null;

    public async Task<ChatIdentity?> ConfirmFromStartAsync(long chatId, UserIdentity user, CancellationToken ct = default)
    {
        var row = await subscriberRepository.GetAsync(user.Id, chatId, ct);
        if (row is null)
        {
            return null;
        }

        await CleanupPromptAsync(row, ct);
        return await ChatIdentity.FromAsync(chatId, managedChatsRepository, ct);
    }

    public async Task HandleChatMemberUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default)
    {
        if (update.Chat.Type == ChatType.Private)
        {
            return;
        }

        if (update.NewChatMember.Status is not (ChatMemberStatus.Left or ChatMemberStatus.Kicked))
        {
            return;
        }

        var user = update.NewChatMember.User;
        if (await subscriberRepository.DeleteAsync(user.Id, update.Chat.Id, ct))
        {
            pipelineMetrics.RecordBanCelebrationSubscription("left");
            logger.LogInformation(
                "Removed DM ban celebration subscription for {User} who left {Chat}",
                user.ToLogInfo(), update.Chat.ToLogInfo());
        }
    }

    public async Task HandleBotMembershipUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default)
    {
        if (update.Chat.Type != ChatType.Private || update.NewChatMember.Status != ChatMemberStatus.Kicked)
        {
            return;
        }

        var user = UserIdentity.From(update.From);
        await telegramUserRepository.DisableBotDmAsync(user.Id, ct);
        await RemoveAllForUserAsync(user, SubscriptionRemovalReason.Blocked, ct);
    }

    public async Task RemoveAllForUserAsync(UserIdentity user, SubscriptionRemovalReason reason, CancellationToken ct = default)
    {
        var removed = await subscriberRepository.DeleteAllForUserAsync(user.Id, ct);
        if (removed == 0)
        {
            return;
        }

        pipelineMetrics.RecordBanCelebrationSubscription(reason.ToString().ToLowerInvariant(), removed);
        logger.LogInformation(
            "Removed {Count} DM ban celebration subscription(s) for {User} ({Reason})",
            removed, user.ToLogInfo(), reason);
    }

    private async Task PostStartPromptAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct)
    {
        // One prompt per (user, chat): close any prompt a previous /dmcelebrations on left open.
        var existing = await subscriberRepository.GetAsync(user.Id, chat.Id, ct);
        if (existing is not null)
        {
            await CleanupPromptAsync(existing, ct);
        }

        var bot = await userService.GetMeAsync(ct);
        var link = DmCelebrationDeepLink.Build(bot.Username!, chat.Id);
        var text = new TelegramMessageBuilder()
            .Mention(user)
            .Text($", tap below so I can send you {chat.ChatName ?? "this chat"}'s ban celebrations.")
            .Build();
        var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("🎉 Open a chat with me", link));

        var prompt = await messageService.SendAndSaveMessageAsync(chat.Id, text, replyMarkup: keyboard, cancellationToken: ct);
        var jobId = await jobScheduler.ScheduleJobAsync(
            DeleteMessageJobName,
            new DeleteMessagePayload(chat.Id, prompt.MessageId, PromptTimeoutReason),
            PromptLifetimeSeconds,
            DeduplicationKeys.None,
            ct);

        await subscriberRepository.SetPromptAsync(user.Id, chat.Id, prompt.MessageId, jobId, ct);
    }

    /// <summary>
    /// Cancels the pending delete job, deletes the prompt, and clears the columns. Tolerates a
    /// job or message that is already gone (the timeout job ran, or the message was removed by hand).
    /// </summary>
    private async Task CleanupPromptAsync(BanCelebrationSubscriber row, CancellationToken ct)
    {
        if (row.PromptMessageId is null && row.PromptDeleteJobId is null)
        {
            return;
        }

        if (row.PromptDeleteJobId is { } jobId)
        {
            await jobScheduler.CancelJobAsync(jobId, ct);
        }

        if (row.PromptMessageId is { } messageId)
        {
            await messageService.DeleteAndMarkMessageAsync(row.ChatId, messageId, PromptCleanupSource, ct);
        }

        await subscriberRepository.ClearPromptAsync(row.TelegramUserId, row.ChatId, ct);
    }
}
```

`ChatIdentity.ToLogInfo()` and `UserIdentity.ToLogInfo()` come from `TelegramGroupsAdmin.Core.Extensions`. The `Chat.ToLogInfo()` overload used for `update.Chat` comes from `TelegramGroupsAdmin.Telegram.Extensions`, the same one `WelcomeService` uses.

- [ ] **Step 6: Register the service**

In `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`, next to line 161 (`IBanCelebrationService`):

```csharp
            services.AddScoped<IBanCelebrationSubscriptionService, BanCelebrationSubscriptionService>(); // DM celebration opt-in rules
```

Add `using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~DmCelebrations"`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(telegram): add DM ban celebration subscription service

Owns subscribe/unsubscribe, the 60s self-cleaning start prompt and its
/start confirmation, and removal on leave, kick, ban or bot block. The
dmcel_{chatId} deep link carries only the chat id.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 6: Animation payloads and the shared `NotificationDmDispatcher`

**Files:**
- Create: `TelegramGroupsAdmin/Services/Notifications/NotificationAnimation.cs`
- Create: `TelegramGroupsAdmin/Services/Notifications/NotificationDmDispatcher.cs`
- Modify: `TelegramGroupsAdmin/Services/Notifications/NotificationPayload.cs`
- Modify: `TelegramGroupsAdmin/Services/Notifications/NotificationPayloadBuilder.cs`
- Modify: `TelegramGroupsAdmin/Services/AdminNotificationService.cs`
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` (near line 158)
- Test: `TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationDmDispatcherTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Services/Notifications/AdminNotificationServiceRoutingTests.cs` (constructor only)

**Interfaces:**
- Consumes: `IBotDmService.SendDmWithAnimationEntitiesAsync` (Task 4); `NotificationRenderer.ToTelegramMessage(NotificationPayload)`; `UserIdentity.FromAsync(long, ITelegramUserRepository, CancellationToken)`.
- Produces:
  - `internal sealed record NotificationAnimation(string Path, string? FileId)`
  - `NotificationPayload.Animation` (`NotificationAnimation?`)
  - `NotificationPayloadBuilder.WithAnimation(string path, string? fileId)`
  - `internal sealed class NotificationDmDispatcher(IBotDmService dmService, ITelegramUserRepository telegramUserRepository)` with:
    - `Task<DmDeliveryResult> DispatchAsync(long telegramId, NotificationPayload payload, InlineKeyboardMarkup? keyboard, CancellationToken ct)`
    - `Task<DmDeliveryResult> DispatchAsync(UserIdentity recipient, NotificationPayload payload, InlineKeyboardMarkup? keyboard, CancellationToken ct)`
  - `AdminNotificationService` becomes `internal sealed`, and its constructor takes `NotificationDmDispatcher` in place of `IBotDmService` and `ITelegramUserRepository`.

- [ ] **Step 1: Write the failing dispatcher tests**

`TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationDmDispatcherTests.cs`:

```csharp
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class NotificationDmDispatcherTests
{
    private static readonly UserIdentity Recipient = new(42L, "Kim", null, "kim");

    private IBotDmService _dm = null!;
    private ITelegramUserRepository _users = null!;
    private NotificationDmDispatcher _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _dm = Substitute.For<IBotDmService>();
        _users = Substitute.For<ITelegramUserRepository>();
        _sut = new NotificationDmDispatcher(_dm, _users);
    }

    [Test]
    public async Task DispatchAsync_AnimationPayload_SendsAnimationWithHeaderAndCaption()
    {
        var payload = NotificationPayloadBuilder.Create("Workshop Alumni")
            .WithText("Spammer got banned!")
            .WithAnimation("/data/media/ban-gifs/1.gif", "file-1")
            .Build();

        await _sut.DispatchAsync(Recipient, payload, keyboard: null, CancellationToken.None);

        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(
            Recipient,
            Arg.Is<TelegramMessage>(m => m!.Text.StartsWith("Workshop Alumni") && m.Text.Contains("Spammer got banned!")),
            "file-1",
            "/data/media/ban-gifs/1.gif",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_TextOnlyPayload_SendsEntitiesDm()
    {
        var payload = NotificationPayloadBuilder.Create("Subject").WithText("body").Build();

        await _sut.DispatchAsync(Recipient, payload, keyboard: null, CancellationToken.None);

        await _dm.Received(1).SendDmWithEntitiesAsync(Recipient, "notification", Arg.Any<string>(),
            Arg.Any<IReadOnlyList<MessageEntity>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_PhotoPayload_SendsMediaDm()
    {
        var payload = NotificationPayloadBuilder.Create("Subject").WithPhoto("/p.jpg").Build();

        await _sut.DispatchAsync(Recipient, payload, keyboard: null, CancellationToken.None);

        await _dm.Received(1).SendDmWithMediaAndKeyboardEntitiesAsync(Recipient, "notification", Arg.Any<string>(),
            Arg.Any<IReadOnlyList<MessageEntity>>(), "/p.jpg", null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_ByTelegramId_ResolvesIdentityFromRepository()
    {
        var payload = NotificationPayloadBuilder.Create("Subject").WithText("body").Build();

        await _sut.DispatchAsync(42L, payload, keyboard: null, CancellationToken.None);

        await _users.Received(1).GetByTelegramIdAsync(42L, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~NotificationDmDispatcherTests"`
Expected: FAIL. The build breaks because `NotificationDmDispatcher` and `WithAnimation` do not exist.

- [ ] **Step 3: Add animation to the payload model**

`NotificationAnimation.cs`:

```csharp
namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// An animation (GIF) attached to a notification: the full path on disk for uploads and an
/// optional cached Telegram file_id that is preferred when present.
/// </summary>
internal sealed record NotificationAnimation(string Path, string? FileId);
```

In `NotificationPayload.cs`, add `public NotificationAnimation? Animation { get; init; }`.

In `NotificationPayloadBuilder.cs`, add a field `private NotificationAnimation? _animation;`, this method, and `Animation = _animation` in `Build()`:

```csharp
    public NotificationPayloadBuilder WithAnimation(string path, string? fileId)
    {
        _animation = new NotificationAnimation(path, fileId);
        return this;
    }
```

- [ ] **Step 4: Create the dispatcher**

`NotificationDmDispatcher.cs`:

```csharp
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// The single path from a <see cref="NotificationPayload"/> to a Telegram DM, shared by the
/// admin- and user-facing notification services. Renders once and picks the DM overload:
/// animation, media/keyboard, or text.
/// </summary>
internal sealed class NotificationDmDispatcher(
    IBotDmService dmService,
    ITelegramUserRepository telegramUserRepository)
{
    private const string NotificationType = "notification";

    public async Task<DmDeliveryResult> DispatchAsync(
        long telegramId,
        NotificationPayload payload,
        InlineKeyboardMarkup? keyboard,
        CancellationToken ct)
    {
        var recipient = await UserIdentity.FromAsync(telegramId, telegramUserRepository, ct);
        return await DispatchAsync(recipient, payload, keyboard, ct);
    }

    public Task<DmDeliveryResult> DispatchAsync(
        UserIdentity recipient,
        NotificationPayload payload,
        InlineKeyboardMarkup? keyboard,
        CancellationToken ct)
    {
        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        if (payload.Animation is { } animation)
        {
            return dmService.SendDmWithAnimationEntitiesAsync(recipient, rendered, animation.FileId, animation.Path, ct);
        }

        if (keyboard != null || !string.IsNullOrWhiteSpace(payload.PhotoPath) || !string.IsNullOrWhiteSpace(payload.VideoPath))
        {
            return dmService.SendDmWithMediaAndKeyboardEntitiesAsync(
                recipient,
                NotificationType,
                rendered.Text,
                rendered.Entities,
                photoPath: payload.PhotoPath,
                videoPath: payload.VideoPath,
                keyboard: keyboard,
                cancellationToken: ct);
        }

        return dmService.SendDmWithEntitiesAsync(recipient, NotificationType, rendered.Text, rendered.Entities, cancellationToken: ct);
    }
}
```

- [ ] **Step 5: Point `AdminNotificationService` at the dispatcher**

In `AdminNotificationService.cs`:
1. Change `public sealed class AdminNotificationService` to `internal sealed class AdminNotificationService`.
2. Replace the constructor parameter `IBotDmService dmDeliveryService` with `NotificationDmDispatcher dmDispatcher`, and remove the `ITelegramUserRepository telegramUserRepo` parameter. Replace field `_dmDeliveryService` with `private readonly NotificationDmDispatcher _dmDispatcher;` and delete `_telegramUserRepo`. Line 582 was its only reader. Confirm with `grep -n "_telegramUserRepo\|_dmDeliveryService" TelegramGroupsAdmin/Services/AdminNotificationService.cs`, which should print nothing after the edit.
3. Replace the body of `DispatchEntityDmAsync` with:

```csharp
    private async Task<DmDeliveryResult> DispatchEntityDmAsync(
        long telegramId,
        NotificationPayload payload,
        CancellationToken ct)
    {
        InlineKeyboardMarkup? keyboard = null;
        if (payload.Keyboard is { } kb)
        {
            keyboard = await BuildReportActionKeyboardAsync(
                kb.EntityId, kb.ChatId, kb.UserId, kb.KeyboardType, kb.Outcome, ct);
        }

        return await _dmDispatcher.DispatchAsync(telegramId, payload, keyboard, ct);
    }
```

Update its doc comment to: `Build the admin action keyboard (if any) and hand the payload to the shared dispatcher.`

Check that no other assembly references the concrete class: `git grep -n "AdminNotificationService\b" -- 'TelegramGroupsAdmin.ComponentTests/**' 'TelegramGroupsAdmin.E2ETests/**' 'TelegramGroupsAdmin.IntegrationTests/**'`. Expected: only `IAdminNotificationService` hits. If the concrete type appears, stop and report, because making it internal would break that project.

- [ ] **Step 6: Register the dispatcher and fix the routing-test constructor**

In `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`, directly above the `IAdminNotificationService` registration:

```csharp
            services.AddScoped<NotificationDmDispatcher>(); // Shared payload → Telegram DM path (admin + user notifications)
```

In `AdminNotificationServiceRoutingTests.cs`, change the constructor call to:

```csharp
        _service = new AdminNotificationService(
            _mockPrefsRepo,
            _mockEmailService,
            new NotificationDmDispatcher(_mockDmService, _mockTelegramUserRepo),
            _mockWebPushService,
            _mockTelegramMappingRepo,
            _mockChatAdminsRepo,
            _mockUserRepo,
            _mockCallbackContextRepo,
            _mockLogger);
```

The argument order must match the new constructor. The routing assertions on `_mockDmService` stay unchanged, because the real dispatcher forwards to it.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~Services.Notifications"`
Expected: all pass (dispatcher, routing, renderer).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(notifications): extract shared NotificationDmDispatcher with animation support

Moves payload-to-DM dispatch out of the admin service into one class both
notification audiences will use, and adds animation payloads. The admin
service keeps its report keyboards and audience routing.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 7: `IUserNotificationService`, the fan-out queue, and the worker

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IBanCelebrationGifRepository.cs` and `BanCelebrationGifRepository.cs` (add `GetByIdAsync`)
- Create: `TelegramGroupsAdmin.Core/Services/IUserNotificationService.cs`
- Modify: `TelegramGroupsAdmin.Core/Services/IAdminNotificationService.cs` (turn the Task 1 plain-text mention into `<see cref="IUserNotificationService"/>`)
- Create: `TelegramGroupsAdmin/Services/Notifications/BanCelebrationDmSender.cs`
- Create: `TelegramGroupsAdmin/Services/Notifications/BanCelebrationFanoutItem.cs`
- Create: `TelegramGroupsAdmin/Services/Notifications/IBanCelebrationFanoutQueue.cs`
- Create: `TelegramGroupsAdmin/Services/Notifications/BanCelebrationFanoutQueue.cs`
- Create: `TelegramGroupsAdmin/Services/Notifications/BanCelebrationFanoutProcessor.cs`
- Create: `TelegramGroupsAdmin/Services/Notifications/BanCelebrationFanoutWorker.cs`
- Create: `TelegramGroupsAdmin/Services/UserNotificationService.cs`
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationGifRepositoryGetByIdTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Services/Notifications/BanCelebrationDmSenderTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Services/Notifications/BanCelebrationFanoutProcessorTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Services/Notifications/BanCelebrationFanoutQueueTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Services/UserNotificationServiceTests.cs`

**Interfaces:**
- Consumes: `NotificationDmDispatcher`, `NotificationPayloadBuilder.WithAnimation` (Task 6); `IBanCelebrationSubscriberRepository.GetDeliverableSubscribersAsync` (Task 3); `IBanCelebrationSubscriptionService.RemoveAllForUserAsync(UserIdentity, SubscriptionRemovalReason.Blocked, CancellationToken)` (Task 5); `PipelineMetrics.RecordBanCelebrationDm` (Task 4); `IBanCelebrationGifRepository.GetFullPath(string)`, `UpdateFileIdAsync(int, string, CancellationToken)`.
- Produces:
  - `IBanCelebrationGifRepository.GetByIdAsync(int id, CancellationToken ct = default)` → `Task<BanCelebrationGif?>`
  - `TelegramGroupsAdmin.Core.Services.IUserNotificationService` with two methods:
    - `ValueTask EnqueueBanCelebrationAsync(ChatIdentity chat, string caption, int gifId, CancellationToken cancellationToken = default)`
    - `Task<bool> SendBanCelebrationToBannedUserAsync(ChatIdentity chat, UserIdentity bannedUser, string dmCaption, int gifId, CancellationToken cancellationToken = default)`
  - `internal sealed class BanCelebrationDmSender`: `Task<DmDeliveryResult> SendAsync(UserIdentity recipient, ChatIdentity chat, string caption, BanCelebrationGif gif, CancellationToken ct)`, which updates the cached file_id in the repository and on `gif`
  - `internal sealed record BanCelebrationFanoutItem(ChatIdentity Chat, string Caption, int GifId)`
  - `internal interface IBanCelebrationFanoutQueue { ValueTask EnqueueAsync(BanCelebrationFanoutItem item, CancellationToken ct); ChannelReader<BanCelebrationFanoutItem> Reader { get; } }`
  - `internal sealed class BanCelebrationFanoutProcessor`: `Task ProcessAsync(BanCelebrationFanoutItem item, CancellationToken ct)`

- [ ] **Step 1: Write the failing tests**

`TelegramGroupsAdmin.IntegrationTests/Repositories/BanCelebrationGifRepositoryGetByIdTests.cs`. This uses canonical data. The fixture reads the GIF anchor at runtime instead of hard-coding one of the 92 rows:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// <see cref="IBanCelebrationGifRepository.GetByIdAsync"/> against canonical
/// <c>07_ban_celebration_gifs.sql</c> (92 reference rows). The anchor row is read at runtime.
/// </summary>
[TestFixture]
public class BanCelebrationGifRepositoryGetByIdTests
{
    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private string _tempMediaPath = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
        _tempMediaPath = Path.Combine(Path.GetTempPath(), $"GifGetById_{Guid.NewGuid():N}");

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddHttpClient();
        services.AddSingleton(Substitute.For<IVideoFrameExtractionService>());
        services.AddSingleton(Options.Create(new AppOptions { DataPath = _tempMediaPath }));
        services.AddScoped<IBanCelebrationGifRepository, BanCelebrationGifRepository>();
        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
        if (Directory.Exists(_tempMediaPath))
        {
            Directory.Delete(_tempMediaPath, recursive: true);
        }
    }

    [Test]
    public async Task GetByIdAsync_ExistingCanonicalGif_ReturnsIt()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var anchor = await ctx.BanCelebrationGifs.AsNoTracking().OrderBy(g => g.Id).FirstAsync();
        using var scope = _serviceProvider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();

        var gif = await repo.GetByIdAsync(anchor.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gif, Is.Not.Null);
            Assert.That(gif!.FilePath, Is.EqualTo(anchor.FilePath));
            Assert.That(gif.FileId, Is.EqualTo(anchor.FileId));
        }
    }

    [Test]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var unknownId = await ctx.BanCelebrationGifs.MaxAsync(g => g.Id) + 1;
        using var scope = _serviceProvider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();

        Assert.That(await repo.GetByIdAsync(unknownId), Is.Null);
    }
}
```

`TelegramGroupsAdmin.UnitTests/Services/Notifications/BanCelebrationDmSenderTests.cs`:

```csharp
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class BanCelebrationDmSenderTests
{
    private static readonly UserIdentity Recipient = new(42L, "Kim", null, "kim");

    private IBotDmService _dm = null!;
    private IBanCelebrationGifRepository _gifs = null!;
    private BanCelebrationDmSender _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _dm = Substitute.For<IBotDmService>();
        _gifs = Substitute.For<IBanCelebrationGifRepository>();
        _gifs.GetFullPath(Arg.Any<string>()).Returns(ci => "/data/media/" + ci.Arg<string>());
        _sut = new BanCelebrationDmSender(new NotificationDmDispatcher(_dm, Substitute.For<ITelegramUserRepository>()), _gifs);
    }

    private void DmReturns(DmDeliveryResult result) =>
        _dm.SendDmWithAnimationEntitiesAsync(Arg.Any<UserIdentity>(), Arg.Any<TelegramMessage>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(result);

    [Test]
    public async Task SendAsync_FirstUpload_CachesReturnedFileIdInRepositoryAndOnGif()
    {
        var gif = new BanCelebrationGif { Id = 3, FilePath = "ban-gifs/3.gif", FileId = null };
        DmReturns(new DmDeliveryResult { DmSent = true, AnimationFileId = "new-id" });

        await _sut.SendAsync(Recipient, new ChatIdentity(-100L, "Workshop Alumni"), "banned!", gif, CancellationToken.None);

        await _gifs.Received(1).UpdateFileIdAsync(3, "new-id", Arg.Any<CancellationToken>());
        Assert.That(gif.FileId, Is.EqualTo("new-id"));
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(Recipient, Arg.Any<TelegramMessage>(),
            null, "/data/media/ban-gifs/3.gif", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_SameFileIdReturned_DoesNotRewriteCache()
    {
        var gif = new BanCelebrationGif { Id = 3, FilePath = "ban-gifs/3.gif", FileId = "same" };
        DmReturns(new DmDeliveryResult { DmSent = true, AnimationFileId = "same" });

        await _sut.SendAsync(Recipient, new ChatIdentity(-100L, "Workshop Alumni"), "banned!", gif, CancellationToken.None);

        await _gifs.DidNotReceiveWithAnyArgs().UpdateFileIdAsync(default, default!);
    }

    [Test]
    public async Task SendAsync_ChatWithoutName_UsesChatIdAsHeader()
    {
        var gif = new BanCelebrationGif { Id = 3, FilePath = "ban-gifs/3.gif", FileId = "same" };
        DmReturns(new DmDeliveryResult { DmSent = true, AnimationFileId = "same" });

        await _sut.SendAsync(Recipient, ChatIdentity.FromId(-100123L), "banned!", gif, CancellationToken.None);

        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(Recipient,
            Arg.Is<TelegramMessage>(m => m!.Text.StartsWith("-100123")),
            "same", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
```

`TelegramGroupsAdmin.UnitTests/Services/Notifications/BanCelebrationFanoutProcessorTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class BanCelebrationFanoutProcessorTests
{
    private const long ChatId = -100059667856554L;
    private static readonly ChatIdentity Chat = new(ChatId, "Workshop Alumni");
    private static readonly UserIdentity A = new(1L, "A", null, "a");
    private static readonly UserIdentity B = new(2L, "B", null, "b");
    private static readonly UserIdentity C = new(3L, "C", null, "c");

    private IBanCelebrationSubscriberRepository _subscribers = null!;
    private IBanCelebrationGifRepository _gifs = null!;
    private IBanCelebrationSubscriptionService _subscriptions = null!;
    private IBotDmService _dm = null!;
    private BanCelebrationFanoutProcessor _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _subscribers = Substitute.For<IBanCelebrationSubscriberRepository>();
        _gifs = Substitute.For<IBanCelebrationGifRepository>();
        _subscriptions = Substitute.For<IBanCelebrationSubscriptionService>();
        _dm = Substitute.For<IBotDmService>();
        _gifs.GetFullPath(Arg.Any<string>()).Returns(ci => "/data/media/" + ci.Arg<string>());
        _gifs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 9, FilePath = "ban-gifs/9.gif", FileId = null });

        var sender = new BanCelebrationDmSender(new NotificationDmDispatcher(_dm, Substitute.For<ITelegramUserRepository>()), _gifs);
        _sut = new BanCelebrationFanoutProcessor(_subscribers, _gifs, _subscriptions, sender,
            new PipelineMetrics(), NullLogger<BanCelebrationFanoutProcessor>.Instance);
    }

    private static BanCelebrationFanoutItem Item => new(Chat, "Spammer got banned!", 9);

    private void Subscribers(params UserIdentity[] users) =>
        _subscribers.GetDeliverableSubscribersAsync(ChatId, Arg.Any<CancellationToken>()).Returns(users.ToList());

    private void DmFor(UserIdentity user, DmDeliveryResult result) =>
        _dm.SendDmWithAnimationEntitiesAsync(user, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(result);

    [Test]
    public async Task ProcessAsync_FirstSendUploads_LaterSendsReuseReturnedFileId()
    {
        Subscribers(A, B, C);
        DmFor(A, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });
        DmFor(B, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });
        DmFor(C, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(A, Arg.Any<TelegramMessage>(), null, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(B, Arg.Any<TelegramMessage>(), "fresh", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(C, Arg.Any<TelegramMessage>(), "fresh", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _gifs.Received(1).UpdateFileIdAsync(9, "fresh", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_BlockedSubscriber_RemovesTheirSubscriptionsAndContinues()
    {
        Subscribers(A, B);
        DmFor(A, new DmDeliveryResult { DmSent = false, Failed = true, Blocked = true });
        DmFor(B, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _subscriptions.Received(1).RemoveAllForUserAsync(A, SubscriptionRemovalReason.Blocked, Arg.Any<CancellationToken>());
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(B, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_OneSendThrows_ContinuesWithTheRest()
    {
        Subscribers(A, B);
        _dm.SendDmWithAnimationEntitiesAsync(A, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        DmFor(B, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(B, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_NoDeliverableSubscribers_DoesNotLoadGif()
    {
        Subscribers();

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _gifs.DidNotReceiveWithAnyArgs().GetByIdAsync(default);
    }

    [Test]
    public async Task ProcessAsync_GifDeletedSinceEnqueue_SendsNothing()
    {
        Subscribers(A);
        _gifs.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns((BanCelebrationGif?)null);

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _dm.DidNotReceiveWithAnyArgs().SendDmWithAnimationEntitiesAsync(default!, default!, default, default);
    }
}
```

`TelegramGroupsAdmin.UnitTests/Services/Notifications/BanCelebrationFanoutQueueTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Metrics;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class BanCelebrationFanoutQueueTests
{
    [Test]
    public async Task EnqueueAsync_BeyondCapacity_DropsOldestItem()
    {
        var queue = new BanCelebrationFanoutQueue(new PipelineMetrics(), NullLogger<BanCelebrationFanoutQueue>.Instance);

        for (var i = 0; i <= BanCelebrationFanoutQueue.Capacity; i++)
        {
            await queue.EnqueueAsync(new BanCelebrationFanoutItem(ChatIdentity.FromId(i), "c", i), CancellationToken.None);
        }

        Assert.That(queue.Reader.TryRead(out var first), Is.True);
        Assert.That(first!.GifId, Is.EqualTo(1), "item 0 should have been dropped");
    }
}
```

`TelegramGroupsAdmin.UnitTests/Services/UserNotificationServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Services;

[TestFixture]
public class UserNotificationServiceTests
{
    private BanCelebrationFanoutQueue _queue = null!;
    private IBanCelebrationGifRepository _gifs = null!;
    private IBotDmService _dm = null!;
    private UserNotificationService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _queue = new BanCelebrationFanoutQueue(new PipelineMetrics(), NullLogger<BanCelebrationFanoutQueue>.Instance);
        _gifs = Substitute.For<IBanCelebrationGifRepository>();
        _dm = Substitute.For<IBotDmService>();
        _gifs.GetFullPath(Arg.Any<string>()).Returns(ci => "/data/media/" + ci.Arg<string>());
        var sender = new BanCelebrationDmSender(new NotificationDmDispatcher(_dm, Substitute.For<ITelegramUserRepository>()), _gifs);
        _sut = new UserNotificationService(_queue, _gifs, sender);
    }

    [Test]
    public async Task EnqueueBanCelebrationAsync_WritesOneItemToTheQueue()
    {
        var chat = new ChatIdentity(-100L, "Workshop Alumni");

        await _sut.EnqueueBanCelebrationAsync(chat, "banned!", 9);

        Assert.That(_queue.Reader.TryRead(out var item), Is.True);
        Assert.That(item, Is.EqualTo(new BanCelebrationFanoutItem(chat, "banned!", 9)));
    }

    [Test]
    public async Task SendBanCelebrationToBannedUserAsync_SendsAnimationAndReturnsDelivery()
    {
        _gifs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 9, FilePath = "ban-gifs/9.gif", FileId = "cached" });
        _dm.SendDmWithAnimationEntitiesAsync(Arg.Any<UserIdentity>(), Arg.Any<TelegramMessage>(), "cached", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true, AnimationFileId = "cached" });

        var sent = await _sut.SendBanCelebrationToBannedUserAsync(
            new ChatIdentity(-100L, "Workshop Alumni"), new UserIdentity(7L, "Bad", null, null), "You got banned!", 9);

        Assert.That(sent, Is.True);
    }

    [Test]
    public async Task SendBanCelebrationToBannedUserAsync_UnknownGif_ReturnsFalse()
    {
        var sent = await _sut.SendBanCelebrationToBannedUserAsync(
            new ChatIdentity(-100L, "Workshop Alumni"), new UserIdentity(7L, "Bad", null, null), "You got banned!", 9);

        Assert.That(sent, Is.False);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BanCelebrationDmSenderTests|FullyQualifiedName~BanCelebrationFanout|FullyQualifiedName~UserNotificationServiceTests"`
Expected: FAIL. The build breaks because the types do not exist.

- [ ] **Step 3: Add `GetByIdAsync` to the GIF repository**

Interface, after `GetRandomAsync`:

```csharp
    /// <summary>
    /// Gets a GIF by id, or null if it no longer exists
    /// </summary>
    Task<BanCelebrationGif?> GetByIdAsync(int id, CancellationToken ct = default);
```

Implementation, after `GetRandomAsync`:

```csharp
    public async Task<BanCelebrationGif?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dto = await context.BanCelebrationGifs.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        return dto?.ToModel();
    }
```

- [ ] **Step 4: Create the Core interface**

`TelegramGroupsAdmin.Core/Services/IUserNotificationService.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// User-facing notifications: Telegram DMs to individual users who opted in (or who are the
/// subject of the event). No web-user preferences, email, or push — those are admin concerns
/// on <see cref="IAdminNotificationService"/>. Planned home for today's user DMs on the legacy
/// INotificationOrchestrator path when that path is retired.
/// </summary>
public interface IUserNotificationService
{
    /// <summary>
    /// Queues a ban celebration for delivery to the chat's DM subscribers. Returns once queued;
    /// subscribers are resolved and messaged in the background.
    /// </summary>
    ValueTask EnqueueBanCelebrationAsync(ChatIdentity chat, string caption, int gifId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the ban celebration straight to the banned user. Returns true when the DM was delivered.
    /// </summary>
    Task<bool> SendBanCelebrationToBannedUserAsync(ChatIdentity chat, UserIdentity bannedUser, string dmCaption, int gifId, CancellationToken cancellationToken = default);
}
```

In `IAdminNotificationService.cs`, change the plain-text `IUserNotificationService` from Task 1 Step 3 to `<see cref="IUserNotificationService"/>`.

- [ ] **Step 5: Create the sender, queue, processor, and worker**

`BanCelebrationDmSender.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Sends one ban celebration animation DM (chat name header + caption) and keeps the GIF's
/// cached Telegram file_id current — the single place both the subscriber fan-out and the
/// banned-user DM go through.
/// </summary>
internal sealed class BanCelebrationDmSender(
    NotificationDmDispatcher dispatcher,
    IBanCelebrationGifRepository gifRepository)
{
    public async Task<DmDeliveryResult> SendAsync(
        UserIdentity recipient,
        ChatIdentity chat,
        string caption,
        BanCelebrationGif gif,
        CancellationToken ct)
    {
        var payload = NotificationPayloadBuilder.Create(chat.ChatName ?? chat.Id.ToString())
            .WithText(caption)
            .WithAnimation(gifRepository.GetFullPath(gif.FilePath), gif.FileId)
            .Build();

        var result = await dispatcher.DispatchAsync(recipient, payload, keyboard: null, ct);

        if (result.AnimationFileId is { } returned && returned != gif.FileId)
        {
            await gifRepository.UpdateFileIdAsync(gif.Id, returned, ct);
            gif.FileId = returned;
        }

        return result;
    }
}
```

`BanCelebrationFanoutItem.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// One celebration awaiting subscriber fan-out. Carries ids, not a GIF snapshot, so the worker
/// always sees the latest cached file_id.
/// </summary>
internal sealed record BanCelebrationFanoutItem(ChatIdentity Chat, string Caption, int GifId);
```

`IBanCelebrationFanoutQueue.cs`:

```csharp
using System.Threading.Channels;

namespace TelegramGroupsAdmin.Services.Notifications;

internal interface IBanCelebrationFanoutQueue
{
    ValueTask EnqueueAsync(BanCelebrationFanoutItem item, CancellationToken ct);

    ChannelReader<BanCelebrationFanoutItem> Reader { get; }
}
```

`BanCelebrationFanoutQueue.cs`:

```csharp
using System.Threading.Channels;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Telegram.Metrics;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Singleton bounded, in-memory queue of celebrations awaiting subscriber fan-out. Drops the
/// oldest item when full — a minutes-old celebration during a raid isn't worth delivering — and
/// loses in-flight items on restart, which is acceptable for GIFs.
/// </summary>
internal sealed class BanCelebrationFanoutQueue : IBanCelebrationFanoutQueue
{
    public const int Capacity = 100;

    private readonly Channel<BanCelebrationFanoutItem> _channel;

    public BanCelebrationFanoutQueue(PipelineMetrics pipelineMetrics, ILogger<BanCelebrationFanoutQueue> logger)
    {
        _channel = Channel.CreateBounded<BanCelebrationFanoutItem>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            },
            dropped =>
            {
                pipelineMetrics.RecordBanCelebrationDm("dropped");
                logger.LogWarning(
                    "Ban celebration fan-out queue full; dropped the oldest celebration for {Chat}",
                    dropped.Chat.ToLogDebug());
            });
    }

    public ChannelReader<BanCelebrationFanoutItem> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(BanCelebrationFanoutItem item, CancellationToken ct) =>
        _channel.Writer.WriteAsync(item, ct);
}
```

`BanCelebrationFanoutProcessor.cs`:

```csharp
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Delivers one queued celebration to every subscriber who can receive DMs, one at a time,
/// paced under Telegram's global rate limit. Subscribers are resolved now, not at enqueue time,
/// so anyone who left, was banned, or unsubscribed in between is skipped. The first send
/// uploads the GIF; every later send reuses the returned file_id.
/// </summary>
internal sealed class BanCelebrationFanoutProcessor(
    IBanCelebrationSubscriberRepository subscriberRepository,
    IBanCelebrationGifRepository gifRepository,
    IBanCelebrationSubscriptionService subscriptionService,
    BanCelebrationDmSender sender,
    PipelineMetrics pipelineMetrics,
    ILogger<BanCelebrationFanoutProcessor> logger)
{
    private static readonly TimeSpan SendSpacing = TimeSpan.FromMilliseconds(50);

    public async Task ProcessAsync(BanCelebrationFanoutItem item, CancellationToken ct)
    {
        var subscribers = await subscriberRepository.GetDeliverableSubscribersAsync(item.Chat.Id, ct);
        if (subscribers.Count == 0)
        {
            return;
        }

        var gif = await gifRepository.GetByIdAsync(item.GifId, ct);
        if (gif is null)
        {
            logger.LogWarning("Ban celebration GIF {GifId} no longer exists; skipping fan-out for {Chat}",
                item.GifId, item.Chat.ToLogDebug());
            return;
        }

        int sent = 0, blocked = 0, failed = 0;
        for (var i = 0; i < subscribers.Count; i++)
        {
            if (i > 0)
            {
                await Task.Delay(SendSpacing, ct);
            }

            var subscriber = subscribers[i];
            try
            {
                var result = await sender.SendAsync(subscriber, item.Chat, item.Caption, gif, ct);
                if (result.DmSent)
                {
                    sent++;
                    pipelineMetrics.RecordBanCelebrationDm("sent");
                }
                else if (result.Blocked)
                {
                    blocked++;
                    pipelineMetrics.RecordBanCelebrationDm("blocked");
                    await subscriptionService.RemoveAllForUserAsync(subscriber, SubscriptionRemovalReason.Blocked, ct);
                }
                else
                {
                    failed++;
                    pipelineMetrics.RecordBanCelebrationDm("failed");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                pipelineMetrics.RecordBanCelebrationDm("failed");
                logger.LogWarning(ex, "Ban celebration DM to {User} failed", subscriber.ToLogDebug());
            }
        }

        logger.LogInformation(
            "Ban celebration fan-out for {Chat}: sent {Sent}, blocked {Blocked}, failed {Failed}",
            item.Chat.ToLogInfo(), sent, blocked, failed);
    }
}
```

`BanCelebrationFanoutWorker.cs`:

```csharp
using TelegramGroupsAdmin.Core.Extensions;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Single reader of the fan-out queue. One celebration at a time, each in its own DI scope;
/// a failed item is logged and never stops the loop. Stops only on host shutdown.
/// </summary>
internal sealed class BanCelebrationFanoutWorker(
    IBanCelebrationFanoutQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BanCelebrationFanoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<BanCelebrationFanoutProcessor>();
                    await processor.ProcessAsync(item, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Ban celebration fan-out failed for {Chat}", item.Chat.ToLogDebug());
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown
        }
    }
}
```

The worker's loop is thin. All its behaviour lives in the processor, which is tested directly. If `ILogger`, `IServiceScopeFactory`, or `BackgroundService` are not already covered by the web project's global usings, add `using Microsoft.Extensions.Logging;`, `using Microsoft.Extensions.DependencyInjection;`, and `using Microsoft.Extensions.Hosting;`.

- [ ] **Step 6: Create `UserNotificationService`**

`TelegramGroupsAdmin/Services/UserNotificationService.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <inheritdoc />
internal sealed class UserNotificationService(
    IBanCelebrationFanoutQueue fanoutQueue,
    IBanCelebrationGifRepository gifRepository,
    BanCelebrationDmSender celebrationSender) : IUserNotificationService
{
    public ValueTask EnqueueBanCelebrationAsync(ChatIdentity chat, string caption, int gifId, CancellationToken cancellationToken = default) =>
        fanoutQueue.EnqueueAsync(new BanCelebrationFanoutItem(chat, caption, gifId), cancellationToken);

    public async Task<bool> SendBanCelebrationToBannedUserAsync(
        ChatIdentity chat,
        UserIdentity bannedUser,
        string dmCaption,
        int gifId,
        CancellationToken cancellationToken = default)
    {
        var gif = await gifRepository.GetByIdAsync(gifId, cancellationToken);
        if (gif is null)
        {
            return false;
        }

        var result = await celebrationSender.SendAsync(bannedUser, chat, dmCaption, gif, cancellationToken);
        return result.DmSent;
    }
}
```

- [ ] **Step 7: Register everything**

In `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`, after the `NotificationDmDispatcher` / `IAdminNotificationService` lines:

```csharp
            // User-facing notifications (opt-in DMs) — DM ban celebrations fan out via a single-reader channel
            services.AddScoped<BanCelebrationDmSender>();
            services.AddScoped<IUserNotificationService, UserNotificationService>();
            services.AddScoped<BanCelebrationFanoutProcessor>();
            services.AddSingleton<IBanCelebrationFanoutQueue, BanCelebrationFanoutQueue>();
            services.AddHostedService<BanCelebrationFanoutWorker>();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BanCelebrationDmSenderTests|FullyQualifiedName~BanCelebrationFanout|FullyQualifiedName~UserNotificationServiceTests"` → Expected: all pass. The processor tests take about 0.1s each because of the send pacing.
Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~BanCelebrationGifRepositoryGetByIdTests"` → Expected: 2 pass.
Run: `dotnet build TelegramGroupsAdmin.sln` → Expected: 0 errors. A DI validation error at app start would surface in Task 11.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(notifications): add user notification service with DM celebration fan-out

IUserNotificationService queues subscriber fan-out on a bounded
drop-oldest channel drained by one background worker, and sends the
banned-user celebration directly. Both go through one sender that keeps
the GIF's cached file_id current; blocked subscribers are unsubscribed.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 8: `/dmcelebrations` command and the `/start` confirmation

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/DmCelebrationsCommand.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/CommandNames.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs` (command registrations near line 202)
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/StartCommand.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/Commands/DmCelebrationsCommandTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/Commands/StartCommandDmCelebrationsTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/Commands/HelpCommandTests.cs` (register the new name)

**Interfaces:**
- Consumes: `IBanCelebrationSubscriptionService` (`SubscribeAsync`, `UnsubscribeAsync`, `IsSubscribedAsync`, `ConfirmFromStartAsync`) and `DmCelebrationDeepLink.TryParseChatId` (Task 5); `CommandResult(TelegramMessage Message, bool DeleteCommandMessage, int? DeleteResponseAfterSeconds = null)`.
- Produces: `CommandNames.DmCelebrations = "dmcelebrations"`; the keyed command registration. Because `CommandNames.All` feeds the router, `/help`, and the Telegram command menu, the command appears in all three automatically.

- [ ] **Step 1: Write the failing command tests**

`DmCelebrationsCommandTests.cs`:

```csharp
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

[TestFixture]
public class DmCelebrationsCommandTests
{
    private const long ChatId = -100059667856554L;
    private const long UserId = 42L;

    private IBanCelebrationSubscriptionService _subscriptions = null!;
    private DmCelebrationsCommand _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _subscriptions = Substitute.For<IBanCelebrationSubscriptionService>();
        _sut = new DmCelebrationsCommand(_subscriptions);
    }

    private static Message GroupMessage() => new()
    {
        Id = 10,
        Chat = new Chat { Id = ChatId, Type = ChatType.Supergroup, Title = "Workshop Alumni" },
        From = new User { Id = UserId, FirstName = "Kim" }
    };

    [Test]
    public async Task Execute_OnWithDmsEnabled_ConfirmsSubscription()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.Subscribed);

        var result = await _sut.ExecuteAsync(GroupMessage(), ["on"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("Workshop Alumni"));
        await _subscriptions.Received(1).SubscribeAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), Arg.Is<UserIdentity>(u => u!.Id == UserId), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_OnAwaitingStart_RepliesWithNothingBecauseThePromptIsTheReply()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.AwaitingStart);

        var result = await _sut.ExecuteAsync(GroupMessage(), ["on"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Is.Empty);
    }

    [Test]
    public async Task Execute_UppercaseOnWithTrailingWords_Subscribes()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.Subscribed);

        await _sut.ExecuteAsync(GroupMessage(), ["ON", "please"], PermissionLevel.Member);

        await _subscriptions.ReceivedWithAnyArgs(1).SubscribeAsync(default!, default!);
    }

    [Test]
    public async Task Execute_Off_Unsubscribes()
    {
        var result = await _sut.ExecuteAsync(GroupMessage(), ["off"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("Workshop Alumni"));
        await _subscriptions.Received(1).UnsubscribeAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), Arg.Is<UserIdentity>(u => u!.Id == UserId), Arg.Any<CancellationToken>());
    }

    [TestCase(true, "/dmcelebrations off")]
    [TestCase(false, "/dmcelebrations on")]
    public async Task Execute_NoArgument_ReportsStateWithUsageHint(bool subscribed, string hint)
    {
        _subscriptions.IsSubscribedAsync(ChatId, UserId, Arg.Any<CancellationToken>()).Returns(subscribed);

        var result = await _sut.ExecuteAsync(GroupMessage(), [], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain(hint));
    }

    [Test]
    public async Task Execute_InPrivateChat_RefusesAndTouchesNothing()
    {
        var dm = GroupMessage();
        dm.Chat = new Chat { Id = UserId, Type = ChatType.Private };

        var result = await _sut.ExecuteAsync(dm, ["on"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("in the group"));
        await _subscriptions.DidNotReceiveWithAnyArgs().SubscribeAsync(default!, default!);
    }

    [Test]
    public void Metadata_MatchesTheSpec()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sut.Name, Is.EqualTo("dmcelebrations"));
            Assert.That(_sut.MinPermissionLevel, Is.EqualTo(PermissionLevel.Member));
            Assert.That(_sut.DeleteCommandMessage, Is.True);
            Assert.That(_sut.DeleteResponseAfterSeconds, Is.EqualTo(30));
        }
    }
}
```

`StartCommandDmCelebrationsTests.cs`. Construct `StartCommand` with a substitute for every constructor parameter:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

[TestFixture]
public class StartCommandDmCelebrationsTests
{
    private const long ChatId = -100059667856554L;
    private const long UserId = 42L;

    private IBanCelebrationSubscriptionService _subscriptions = null!;
    private StartCommand _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _subscriptions = Substitute.For<IBanCelebrationSubscriptionService>();
        _sut = new StartCommand(
            NullLogger<StartCommand>.Instance,
            Substitute.For<IWelcomeResponsesRepository>(),
            Substitute.For<ITelegramUserRepository>(),
            Substitute.For<IPendingNotificationsRepository>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<IBotMessageService>(),
            Substitute.For<IBotChatService>(),
            Substitute.For<IBotDmService>(),
            _subscriptions);
    }

    private static Message PrivateStart() => new()
    {
        Id = 1,
        Chat = new Chat { Id = UserId, Type = ChatType.Private },
        From = new User { Id = UserId, FirstName = "Kim" }
    };

    [Test]
    public async Task StartCommand_DmCelebrationsPayload_Subscribed_ConfirmsWithChatName()
    {
        _subscriptions.ConfirmFromStartAsync(ChatId, Arg.Is<UserIdentity>(u => u!.Id == UserId), Arg.Any<CancellationToken>())
            .Returns(new ChatIdentity(ChatId, "Workshop Alumni"));

        var result = await _sut.ExecuteAsync(PrivateStart(), [$"dmcel_{ChatId}"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("Workshop Alumni"));
    }

    [Test]
    public async Task StartCommand_DmCelebrationsPayload_NotSubscribed_RepliesNeutrally()
    {
        var result = await _sut.ExecuteAsync(PrivateStart(), [$"dmcel_{ChatId}"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("/dmcelebrations on"));
    }

    [Test]
    public async Task StartCommand_MalformedDmCelebrationsPayload_FallsThroughToDefaultWelcome()
    {
        var result = await _sut.ExecuteAsync(PrivateStart(), ["dmcel_abc"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("Welcome to TelegramGroupsAdmin Bot"));
        await _subscriptions.DidNotReceiveWithAnyArgs().ConfirmFromStartAsync(default, default!);
    }
}
```

In `HelpCommandTests.cs`, add `"dmcelebrations"` to the public-commands array in `SetUp`: `new[] { "help", "start", "mystatus", "dmcelebrations", "report", "link", "invite" }`. `HelpCommand` resolves every `CommandNames.All` entry with `GetRequiredKeyedService`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~DmCelebrationsCommandTests|FullyQualifiedName~StartCommandDmCelebrationsTests|FullyQualifiedName~HelpCommandTests"`
Expected: FAIL. The build breaks because `DmCelebrationsCommand` does not exist and `StartCommand` has no 9-argument constructor.

- [ ] **Step 3: Add the command name**

In `CommandNames.cs`, add `public const string DmCelebrations = "dmcelebrations";` after `MyStatus`, and append `DmCelebrations` to `All`:

```csharp
        Start, Help, Link, Spam, Ban, Trust, Unban,
        Warn, TempBan, Mute, Report, Invite, Delete, MyStatus, DmCelebrations
```

- [ ] **Step 4: Implement the command**

`DmCelebrationsCommand.cs`:

```csharp
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /dmcelebrations on|off — opt in or out of receiving this chat's ban celebrations by DM.
/// Group-only: posting in the group is what proves membership.
/// </summary>
public sealed class DmCelebrationsCommand(IBanCelebrationSubscriptionService subscriptionService) : IBotCommand
{
    public string Name => CommandNames.DmCelebrations;
    public string Description => "Get this chat's ban celebrations in your DMs (on/off)";
    public string Usage => "/dmcelebrations on|off";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Member;
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => true;
    public int? DeleteResponseAfterSeconds => 30;

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        CancellationToken cancellationToken = default)
    {
        if (message.From is null)
        {
            return Reply(TelegramMessage.Empty);
        }

        if (message.Chat.Type == ChatType.Private)
        {
            return Reply(TelegramMessage.Plain("Run /dmcelebrations on in the group you want celebrations from."));
        }

        var chat = ChatIdentity.From(message.Chat);
        var user = UserIdentity.From(message.From);
        var chatName = chat.ChatName ?? "this chat";

        switch (args.FirstOrDefault()?.ToLowerInvariant())
        {
            case "on":
                var result = await subscriptionService.SubscribeAsync(chat, user, cancellationToken);
                // AwaitingStart: the start prompt (with its button) is already posted and self-cleans.
                return Reply(result == DmCelebrationSubscribeResult.Subscribed
                    ? TelegramMessage.Plain($"✅ You'll get {chatName}'s ban celebrations in your DMs.")
                    : TelegramMessage.Empty);

            case "off":
                await subscriptionService.UnsubscribeAsync(chat, user, cancellationToken);
                return Reply(TelegramMessage.Plain($"🔕 You won't get {chatName}'s ban celebrations in your DMs anymore."));

            default:
                var subscribed = await subscriptionService.IsSubscribedAsync(chat.Id, user.Id, cancellationToken);
                return Reply(TelegramMessage.Plain(subscribed
                    ? $"✅ You're getting {chatName}'s ban celebrations in your DMs. Use /dmcelebrations off to stop."
                    : $"You're not getting {chatName}'s ban celebrations in your DMs. Use /dmcelebrations on to start."));
        }
    }

    private CommandResult Reply(TelegramMessage message) =>
        new(message, DeleteCommandMessage, DeleteResponseAfterSeconds);
}
```

- [ ] **Step 5: Add the `/start` branch**

In `StartCommand.cs`:
1. Add `using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;`.
2. Add a constructor parameter `IBanCelebrationSubscriptionService celebrationSubscriptionService` as the last parameter, stored in a `private readonly IBanCelebrationSubscriptionService _celebrationSubscriptionService;` field.
3. In `ExecuteAsync`, directly before the `// Check if this is a deep link for welcome system` block (so after `EnableBotDmAsync` and pending-notification delivery), insert:

```csharp
        // Deep link from the /dmcelebrations start prompt
        if (args.Length > 0 && message.From != null &&
            DmCelebrationDeepLink.TryParseChatId(args[0], out var celebrationChatId))
        {
            return await HandleDmCelebrationsDeepLinkAsync(message.From, celebrationChatId, cancellationToken);
        }
```

4. Add the handler method:

```csharp
    private async Task<CommandResult> HandleDmCelebrationsDeepLinkAsync(
        User from,
        long chatId,
        CancellationToken cancellationToken)
    {
        var chat = await _celebrationSubscriptionService.ConfirmFromStartAsync(chatId, UserIdentity.From(from), cancellationToken);

        var reply = chat is null
            ? "You're not signed up for ban celebrations from that chat. Run /dmcelebrations on in the group to sign up."
            : $"🎉 You're all set — you'll get {chat.ChatName ?? "that chat"}'s ban celebrations here.";

        return new CommandResult(TelegramMessage.Plain(reply), DeleteCommandMessage, DeleteResponseAfterSeconds);
    }
```

- [ ] **Step 6: Register the command**

In `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`, after the `MyStatusCommand` line:

```csharp
            services.AddKeyedScoped<IBotCommand, DmCelebrationsCommand>(CommandNames.DmCelebrations);
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BotCommands"`
Expected: all pass. `CommandRouterTests` registers every `CommandNames.All` entry in a loop, so it covers the new name.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(commands): add /dmcelebrations on|off and its /start confirmation

Group-only command backed by the subscription service; bare
/dmcelebrations reports the current state. /start dmcel_{chatId} closes
the start prompt and confirms, or replies neutrally when not subscribed.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 9: Wire removal into the update router and both ban paths

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UpdateRouter.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotModerationService.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UpdateRouterTests.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/BotModerationServiceTests.cs`

**Interfaces:**
- Consumes: `IBanCelebrationSubscriptionService.HandleChatMemberUpdateAsync`, `HandleBotMembershipUpdateAsync`, and `RemoveAllForUserAsync(UserIdentity, SubscriptionRemovalReason.Banned, CancellationToken)` (Task 5).
- Produces: a `BotModerationService` constructor with a new parameter `IBanCelebrationSubscriptionService celebrationSubscriptionService`, placed directly after `IBanCelebrationService banCelebrationService`.

- [ ] **Step 1: Write the failing tests**

In `UpdateRouterTests.cs`, add a field `private IBanCelebrationSubscriptionService _mockCelebrationSubscriptions = null!;`. In `SetUp`, next to the other scope wiring, add:

```csharp
        _mockCelebrationSubscriptions = Substitute.For<IBanCelebrationSubscriptionService>();
        _mockScopeServiceProvider.GetService(typeof(IBanCelebrationSubscriptionService)).Returns(_mockCelebrationSubscriptions);
```

Add these tests. `CreateMyChatMemberUpdate` and `CreateChatMemberUpdate` already exist in the file:

```csharp
    [Test]
    public async Task RouteUpdateAsync_WithMyChatMember_RoutesToCelebrationSubscriptions()
    {
        var update = CreateMyChatMemberUpdate();

        await _sut.RouteUpdateAsync(update);

        await _mockCelebrationSubscriptions.Received(1).HandleBotMembershipUpdateAsync(update.MyChatMember!, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteUpdateAsync_WithChatMember_RoutesToCelebrationSubscriptions()
    {
        var update = CreateChatMemberUpdate();

        await _sut.RouteUpdateAsync(update);

        await _mockCelebrationSubscriptions.Received(1).HandleChatMemberUpdateAsync(update.ChatMember!, Arg.Any<CancellationToken>());
    }
```

In `BotModerationServiceTests.cs`, add a field `private IBanCelebrationSubscriptionService _mockCelebrationSubscriptions = null!;`. Create it in `SetUp` with `Substitute.For<IBanCelebrationSubscriptionService>()`, and pass it to the constructor directly after `_mockBanCelebrationService`. Add `using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;`. Then add these tests:

```csharp
    [Test]
    public async Task BanUserAsync_RemovesDmCelebrationSubscriptionsBeforeCelebrating()
    {
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");
        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));
        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        await _orchestrator.BanUserAsync(new BanIntent
        {
            User = UserIdentity.FromId(userId),
            Executor = executor,
            Reason = "Spam violation",
            Chat = ChatIdentity.FromId(-100123456789L)
        });

        Received.InOrder(() =>
        {
            _mockCelebrationSubscriptions.RemoveAllForUserAsync(
                Arg.Is<UserIdentity>(u => u!.Id == userId), SubscriptionRemovalReason.Banned, Arg.Any<CancellationToken>());
            _mockBanCelebrationService.SendBanCelebrationAsync(
                Arg.Any<ChatIdentity>(), Arg.Is<UserIdentity>(u => u!.Id == userId), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task BanUserAsync_WithoutChat_StillRemovesDmCelebrationSubscriptions()
    {
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");
        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));
        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        await _orchestrator.BanUserAsync(new BanIntent
        {
            User = UserIdentity.FromId(userId),
            Executor = executor,
            Reason = "Spam violation"
        });

        await _mockCelebrationSubscriptions.Received(1).RemoveAllForUserAsync(
            Arg.Is<UserIdentity>(u => u!.Id == userId), SubscriptionRemovalReason.Banned, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkAsSpamAndBanAsync_RemovesDmCelebrationSubscriptionsBeforeCelebrating()
    {
        const int messageId = 42;
        const long userId = 12345L;
        const long chatId = -100123456789L;
        var executor = Actor.FromSystem("SpamDetection");
        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());
        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));
        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));
        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        await _orchestrator.MarkAsSpamAndBanAsync(new SpamBanIntent
        {
            User = UserIdentity.FromId(userId),
            MessageId = messageId,
            Chat = ChatIdentity.FromId(chatId),
            Executor = executor,
            Reason = "Spam detected"
        });

        Received.InOrder(() =>
        {
            _mockCelebrationSubscriptions.RemoveAllForUserAsync(
                Arg.Is<UserIdentity>(u => u!.Id == userId), SubscriptionRemovalReason.Banned, Arg.Any<CancellationToken>());
            _mockBanCelebrationService.SendBanCelebrationAsync(
                Arg.Any<ChatIdentity>(), Arg.Is<UserIdentity>(u => u!.Id == userId), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        });
    }
```

If `BanIntent.Chat` has a different property name, check `TelegramGroupsAdmin.Telegram/Services/Moderation/Intents/BanIntent.cs` and use the property `BanUserAsync` reads as `intent.Chat`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UpdateRouterTests|FullyQualifiedName~BotModerationServiceTests"`
Expected: FAIL. The build breaks because `BotModerationService` has no constructor taking the new argument.

- [ ] **Step 3: Route the updates**

In `UpdateRouter.cs`, add `using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;` and resolve the service with the others:

```csharp
        var celebrationSubscriptions = services.GetRequiredService<IBanCelebrationSubscriptionService>();
```

In the `MyChatMember` branch, directly after `await chatService.HandleBotMembershipUpdateAsync(myChatMember, cancellationToken);`:

```csharp
            // Private-chat block/unblock of the bot (blocking drops DM celebration subscriptions)
            await celebrationSubscriptions.HandleBotMembershipUpdateAsync(myChatMember, cancellationToken);
```

In the `ChatMember` branch, directly after `await chatService.HandleAdminStatusChangeAsync(chatMember, cancellationToken);`:

```csharp
            // Leaving or being kicked drops that chat's DM celebration subscription
            await celebrationSubscriptions.HandleChatMemberUpdateAsync(chatMember, cancellationToken);
```

- [ ] **Step 4: Remove subscriptions in both ban paths**

In `BotModerationService.cs`:
1. Add `using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;`.
2. Add the field `private readonly IBanCelebrationSubscriptionService _celebrationSubscriptionService;` under `_banCelebrationService`. Add the constructor parameter `IBanCelebrationSubscriptionService celebrationSubscriptionService` directly after `IBanCelebrationService banCelebrationService`, and assign it.
3. Add this private helper near `RevokeTrustOnBanAsync`:

```csharp
    /// <summary>
    /// Business rule: a banned user loses every DM ban celebration subscription. Must run before
    /// the celebration so the fan-out can never DM the banned user a celebration of their own ban.
    /// </summary>
    private Task RemoveDmCelebrationSubscriptionsAsync(UserIdentity user, CancellationToken cancellationToken) =>
        SafeExecuteAsync(
            () => _celebrationSubscriptionService.RemoveAllForUserAsync(user, SubscriptionRemovalReason.Banned, cancellationToken),
            $"Remove DM celebration subscriptions for user {user.Id}");
```

`SafeExecuteAsync` already takes a `Func<Task>` and a description string at its other call sites. If its signature differs, match the existing calls.

4. In `MarkAsSpamAndBanAsync`, directly before the `// Step 5: Send ban celebration` block, add:

```csharp
        await RemoveDmCelebrationSubscriptionsAsync(intent.User, cancellationToken);
```

5. In `BanUserAsync`, directly before the `// Bug 3 fix: Ban celebration when chat context is provided` block, and outside the `if`, so bans with no chat also clean up, add:

```csharp
        await RemoveDmCelebrationSubscriptionsAsync(intent.User, cancellationToken);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UpdateRouterTests|FullyQualifiedName~BotModerationServiceTests"`
Expected: all pass. The existing "DoesNotCallOtherHandlers" router tests still pass because they assert on the other services.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(moderation): drop DM celebration subscriptions on ban, leave and bot block

Both ban paths (BanUserAsync and the inlined MarkAsSpamAndBanAsync) remove
the user's subscriptions before celebrating. The update router dispatches
member and private-chat bot-membership updates to the subscription service.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 10: Rewrite the celebration pipeline around the guard

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/IBanCelebrationService.cs` (doc comment only)
- Test: `TelegramGroupsAdmin.UnitTests/Services/BanCelebrationServiceTests.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/BanCelebrationServiceTests.cs`

**Interfaces:**
- Consumes: `IBanCelebrationSubscriberRepository.HasDeliverableSubscribersAsync` (Task 3); `IUserNotificationService.EnqueueBanCelebrationAsync` and `SendBanCelebrationToBannedUserAsync` (Task 7).
- Produces: a new constructor `BanCelebrationService(IConfigService, IBanCelebrationGifRepository, IBanCelebrationCaptionRepository, IProfileScanResultsRepository, IBotMessageService, IUserActionsRepository, IBanCelebrationSubscriberRepository, IUserNotificationService, ILogger<BanCelebrationService>, PipelineMetrics)`. `IBotDmService` and `IOptions<AppOptions>` are removed. The return value becomes true when the celebration reached the chat or was queued for subscribers.

- [ ] **Step 1: Write the failing unit tests**

In the unit `BanCelebrationServiceTests.cs`:
1. Remove the `_mockDmService` and `_appOptions` fields and their setup lines.
2. Add the fields `private IBanCelebrationSubscriberRepository _mockSubscriberRepository = null!;` and `private IUserNotificationService _mockUserNotificationService = null!;`. Create both with `Substitute.For<...>()` in `SetUp`, and default the guard to no subscribers:

```csharp
        _mockSubscriberRepository.HasDeliverableSubscribersAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(false);
```

3. Change the construction to:

```csharp
        _sut = new BanCelebrationService(
            _mockConfigService,
            _mockGifRepository,
            _mockCaptionRepository,
            _mockScanRepository,
            _mockMessageService,
            _mockUserActionsRepository,
            _mockSubscriberRepository,
            _mockUserNotificationService,
            _mockLogger,
            _pipelineMetrics);
```

4. Add `using TelegramGroupsAdmin.Core.Services;` if missing, and add this region:

```csharp
    #region Delivery guard

    private void ChatEnabled(bool enabled) =>
        _mockConfigService.GetEffectiveBanCelebrationAsync(Arg.Any<long>())
            .Returns(new BanCelebrationConfig { Enabled = enabled, TriggerOnAutoBan = true, TriggerOnManualBan = true, SendToBannedUser = false });

    private void HasSubscribers(bool has) =>
        _mockSubscriberRepository.HasDeliverableSubscribersAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(has);

    [Test]
    public async Task Guard_ChatDisabledAndNoSubscribers_ClaimsNothing()
    {
        ChatEnabled(false);
        HasSubscribers(false);

        var result = await _sut.SendBanCelebrationAsync(TestChat, TestBannedUser, isAutoBan: true);

        Assert.That(result, Is.False);
        await _mockGifRepository.DidNotReceiveWithAnyArgs().ClaimNextForCycleAsync();
        await _mockCaptionRepository.DidNotReceiveWithAnyArgs().ClaimNextForCycleAsync();
    }

    [Test]
    public async Task Guard_ChatDisabledWithSubscribers_QueuesFanoutWithoutPostingToChat()
    {
        ChatEnabled(false);
        HasSubscribers(true);
        SeedOneGifAndOneCaption("🔨 {username} banned!");

        var result = await _sut.SendBanCelebrationAsync(TestChat, TestBannedUser, isAutoBan: true);

        Assert.That(result, Is.True);
        await _mockMessageService.DidNotReceiveWithAnyArgs().SendAndSaveAnimationAsync(default, default!, default(TelegramMessage)!);
        await _mockUserNotificationService.Received(1).EnqueueBanCelebrationAsync(TestChat, "🔨 Bad User banned!", 1, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Guard_ChatEnabledWithSubscribers_PostsToChatAndQueuesFanout()
    {
        ChatEnabled(true);
        HasSubscribers(true);
        SeedOneGifAndOneCaption("🔨 {username} banned!");

        var result = await _sut.SendBanCelebrationAsync(TestChat, TestBannedUser, isAutoBan: true);

        Assert.That(result, Is.True);
        await _mockMessageService.ReceivedWithAnyArgs(1).SendAndSaveAnimationAsync(default, default!, default(TelegramMessage)!);
        await _mockUserNotificationService.Received(1).EnqueueBanCelebrationAsync(TestChat, Arg.Any<string>(), 1, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Guard_ChatEnabledNoSubscribers_PostsToChatOnly()
    {
        ChatEnabled(true);
        HasSubscribers(false);
        SeedOneGifAndOneCaption("🔨 {username} banned!");

        await _sut.SendBanCelebrationAsync(TestChat, TestBannedUser, isAutoBan: true);

        await _mockUserNotificationService.DidNotReceiveWithAnyArgs().EnqueueBanCelebrationAsync(default!, default!, default);
    }

    [Test]
    public async Task Guard_TriggerOffButSubscribers_SkipsChatButStillQueuesFanout()
    {
        _mockConfigService.GetEffectiveBanCelebrationAsync(Arg.Any<long>())
            .Returns(new BanCelebrationConfig { Enabled = true, TriggerOnAutoBan = false, TriggerOnManualBan = true });
        HasSubscribers(true);
        SeedOneGifAndOneCaption("🔨 {username} banned!");

        await _sut.SendBanCelebrationAsync(TestChat, TestBannedUser, isAutoBan: true);

        await _mockMessageService.DidNotReceiveWithAnyArgs().SendAndSaveAnimationAsync(default, default!, default(TelegramMessage)!);
        await _mockUserNotificationService.ReceivedWithAnyArgs(1).EnqueueBanCelebrationAsync(default!, default!, default);
    }

    #endregion
```

The expected caption `"🔨 Bad User banned!"` assumes `TestBannedUser.DisplayName` renders as `"Bad User"`. `UserIdentity(456, "Bad", "User", null)` does, and the existing masking tests rely on the same thing. If it renders differently, assert with `Arg.Is<string>(s => s!.Contains("banned!"))`.

In the integration `BanCelebrationServiceTests.cs`:
1. Replace `_mockDmService` with `private IUserNotificationService? _mockUserNotificationService;`, created with `Substitute.For<IUserNotificationService>()`. Replace `services.AddSingleton(_mockDmService);` with `services.AddSingleton(_mockUserNotificationService);` and register the real repository with `services.AddScoped<IBanCelebrationSubscriberRepository, BanCelebrationSubscriberRepository>();`. This fixture uses the empty template, so the table is empty and the guard falls back to chat-only. That keeps the fixture's existing behaviour.
2. Rewrite the four DM tests in `#region DM to Banned User Tests` to assert on the new seam. For example, `SendBanCelebrationAsync_SendToBannedUserEnabled_AttemptsDmDelivery` becomes:

```csharp
        _mockUserNotificationService!.SendBanCelebrationToBannedUserAsync(
            Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

        await _mockUserNotificationService!.Received(1).SendBanCelebrationToBannedUserAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == TestChatId), Arg.Is<UserIdentity>(u => u!.Id == TestUserId),
            "You got banned!", Arg.Any<int>(), Arg.Any<CancellationToken>());
```

`"You got banned!"` is the seeded caption's DM text. The two "Skips" tests become `DidNotReceiveWithAnyArgs().SendBanCelebrationToBannedUserAsync(default!, default!, default!, default)`. `DmFails_StillReturnsTrue` stubs `.Returns(false)` and keeps `Assert.That(result, Is.True)`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BanCelebrationServiceTests"`
Expected: FAIL. The build breaks because no constructor matches.

- [ ] **Step 3: Rewrite `BanCelebrationService`**

Change the primary constructor to:

```csharp
public class BanCelebrationService(
    IConfigService configService,
    IBanCelebrationGifRepository gifRepository,
    IBanCelebrationCaptionRepository captionRepository,
    IProfileScanResultsRepository scanRepository,
    IBotMessageService messageService,
    IUserActionsRepository userActionsRepository,
    IBanCelebrationSubscriberRepository subscriberRepository,
    IUserNotificationService userNotificationService,
    ILogger<BanCelebrationService> logger,
    PipelineMetrics pipelineMetrics) : IBanCelebrationService
```

Delete the `_mediaBasePath` field, which has no readers. Remove the `using` lines that are now unused (`Microsoft.Extensions.Options`, and `TelegramGroupsAdmin.Configuration` if nothing else in the file needs it). Add `using TelegramGroupsAdmin.Core.Services;` if missing.

Replace everything in `SendBanCelebrationAsync`'s `try` block from the start through the `// Build the chat caption` assignment and the send, down to `return true;`, with the following. The masking block (`welcomeConfig` … `displayedName`) and the `RecordMaskedUsername` block stay exactly as they are, in the marked position:

```csharp
            var config = await configService.GetEffectiveBanCelebrationAsync(chat.Id, cancellationToken)
                         ?? BanCelebrationConfig.Default;

            // The chat post honours Enabled + the trigger flags; DM subscribers get every celebration.
            var triggerAllowed = isAutoBan ? config.TriggerOnAutoBan : config.TriggerOnManualBan;
            var postToChat = config.Enabled && triggerAllowed;
            var hasSubscribers = await subscriberRepository.HasDeliverableSubscribersAsync(chat.Id, cancellationToken);

            // Rotation claims are durable DB stamps: never claim for a celebration nobody will see.
            if (!postToChat && !hasSubscribers)
            {
                logger.LogDebug("Ban celebration skipped for {Chat}: chat post off and no DM subscribers", chat.ToLogDebug());
                return false;
            }

            var gif = await GetNextGifAsync(cancellationToken);
            if (gif == null)
            {
                logger.LogDebug("No ban celebration GIFs available, skipping celebration");
                return false;
            }

            var caption = await GetNextCaptionAsync(cancellationToken);
            if (caption == null)
            {
                logger.LogDebug("No ban celebration captions available, skipping celebration");
                return false;
            }

            var banCount = await GetTodaysBanCountAsync(cancellationToken);

            // [unchanged masking block: welcomeConfig … displayedName, and the RecordMaskedUsername if-block]

            var chatCaption = ReplacePlaceholders(
                caption.Text,
                displayedName,
                chat.ChatName ?? chat.Id.ToString(),
                banCount);

            var delivered = false;

            if (postToChat)
            {
                var sentMessage = await SendGifToChatAsync(chat, gif, TelegramMessage.Plain(chatCaption), cancellationToken);
                if (sentMessage != null)
                {
                    if (string.IsNullOrEmpty(gif.FileId) && sentMessage.Animation?.FileId != null)
                    {
                        await gifRepository.UpdateFileIdAsync(gif.Id, sentMessage.Animation.FileId, cancellationToken);
                    }

                    logger.LogInformation(
                        "Ban celebration sent to {Chat}: GIF={GifId}, Caption={CaptionId}, User={User}",
                        chat.ToLogInfo(), gif.Id, caption.Id, bannedUser.ToLogInfo());

                    if (config.SendToBannedUser)
                    {
                        await TrySendDmToBannedUserAsync(chat, bannedUser, gif, caption, banCount, cancellationToken);
                    }

                    delivered = true;
                }
            }

            if (hasSubscribers)
            {
                // Subscribers get exactly the chat's (masked) caption; the worker adds the chat header.
                await userNotificationService.EnqueueBanCelebrationAsync(chat, chatCaption, gif.Id, cancellationToken);
                delivered = true;
            }

            return delivered;
```

Replace `TrySendDmToBannedUserAsync`'s body after the two welcome-mode checks. The file-path, extension, and `SendDmWithMediaEntitiesAsync` code all go:

```csharp
            // Build the DM caption (uses "You" grammar)
            var dmCaption = ReplacePlaceholders(caption.DmText, "You", chat.ChatName ?? chat.Id.ToString(), banCount);

            var sent = await userNotificationService.SendBanCelebrationToBannedUserAsync(
                chat, bannedUser, dmCaption, gif.Id, cancellationToken);

            if (sent)
            {
                logger.LogInformation("Ban celebration DM sent to banned user {User}", bannedUser.ToLogInfo());
            }
            else
            {
                logger.LogDebug("Ban celebration DM to banned user {UserId} was not delivered", bannedUser.Id);
            }
```

Update the class `<summary>` to say that the service posts to the chat when enabled and queues DM subscriber delivery. In `IBanCelebrationService.cs`, update the `SendBanCelebrationAsync` doc so that it returns true when the celebration was posted to the chat or queued for DM subscribers.

- [ ] **Step 4: Remove the registration fallout**

Run: `dotnet build TelegramGroupsAdmin.sln`. Expected: 0 errors. `IBanCelebrationService` is resolved by DI, so no other call site constructs it by hand. If a test project fails to compile on the old constructor, fix it the same way as Step 1.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BanCelebrationServiceTests"` → Expected: all pass, including the existing masking and caching tests.
Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~BanCelebrationServiceTests"` → Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(telegram): celebrate for DM subscribers even when the chat post is off

Claims a GIF/caption only when the chat will post it or the chat has DM
subscribers, posts to the chat per Enabled + trigger flags, queues the
same masked caption for subscribers, and routes the banned-user DM
through the user notification service as an animation. Drops the unused
media path field.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 11: Full verification

**Files:** none new (fix-ups only, if something fails).

- [ ] **Step 1: Build clean**

Run: `dotnet build TelegramGroupsAdmin.sln -warnaserror`. Expected: `Build succeeded`, 0 warnings. If the repo's baseline already has warnings, run without `-warnaserror` and confirm that no new warnings come from files this branch touched (`git diff --name-only develop...`).

- [ ] **Step 2: Unit and component suites**

Run: `dotnet test TelegramGroupsAdmin.UnitTests` and `dotnet test TelegramGroupsAdmin.ComponentTests`. Expected: all pass.

- [ ] **Step 3: Integration suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests` (about 60s, needs Docker). Expected: all pass. If a test that is not in this branch fails because of the 4 new canonical rows, for example a whole-table count on a table this branch did not touch, stop and report it. Do not change that test's assertion.

- [ ] **Step 4: DI and startup smoke test**

Run: `dotnet run --project TelegramGroupsAdmin --migrate-only`. Expected: it applies `AddBanCelebrationSubscribers` and exits 0.
Then start the app normally (`dotnet run --project TelegramGroupsAdmin`). The bot is disabled locally, so there is no Telegram conflict. Confirm that the log shows no DI resolution errors for `BanCelebrationFanoutWorker`, `IUserNotificationService`, or `IBanCelebrationSubscriptionService`, then stop it.

- [ ] **Step 5: Dead-code sweep**

Run: `git grep -n "SendDmWithMediaEntitiesAsync" -- '*.cs'`. If the banned-user celebration was its last production caller, check `git grep -n "SendDmWithMediaEntitiesAsync(" -- 'TelegramGroupsAdmin*/**/*.cs' ':!*Tests*'`. If only the interface, implementation, and tests remain, remove the method, its interface member, and its tests in one commit (`refactor: remove SendDmWithMediaEntitiesAsync, orphaned by animation celebrations`). If it still has callers, leave it.

- [ ] **Step 6: Final commit (only if Steps 1–5 changed anything)**

```bash
git add -A
git commit -F- <<'EOF'
chore: verification fix-ups for DM ban celebrations

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```
