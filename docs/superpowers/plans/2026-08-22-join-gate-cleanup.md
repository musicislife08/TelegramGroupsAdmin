# Join-Gate Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a ban/kick close every open report for that user and delete their stranded welcome message, and stop the welcome DM fallback from posting into the group.

**Architecture:** Two cross-cutting business rules move onto `BotModerationService` (the Boss), each executed by a new stateless worker in `Moderation/Actions/`. Report action handlers lose their hand-rolled cleanup and become fetch → moderate → update-own-status. A view migration adds the missing subject-user column for ContentReport so "all report types" is actually queryable.

**Tech Stack:** .NET 10, EF Core 10, PostgreSQL 18, NUnit + NSubstitute (unit), Testcontainers + canonical golden dataset (integration).

**Spec:** `docs/superpowers/specs/2026-08-22-join-gate-cleanup-design.md`

## Global Constraints

- Branch is `fix/join-gate-cleanup`. Never commit to `master` or `develop`. PR targets `develop`.
- Conventional commits (`fix:`, `feat:`, `refactor:`, `test:`, `docs:`).
- Central Package Management — no new NuGet packages are needed for this plan.
- EF Core: modify models + `AppDbContext` FIRST, then `dotnet ef migrations add`. Apply with `dotnet run --migrate`.
- Prefer Fluent API over custom SQL — except the `enriched_reports` view, which is already raw SQL held in `EnrichedReportView.CreateViewSql` and stays that way.
- NSubstitute 6 matcher lambdas are nullable-annotated: `Arg.Is<T>(x => x!.Prop == y)` needs a null-forgiving `!` on the first dereference. Never `?.`.
- Named `cancellationToken:` arguments when passing a token to a method with optional trailing params.
- Integration test data comes from the canonical golden dataset (`TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/`). A SUT write may appear in a test ONLY when that write is the assertion subject.
- No time estimates anywhere.

---

### Task 1: `content_user_id` on the enriched reports view

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/EnrichedReportView.cs`
- Modify: `TelegramGroupsAdmin.Core/Repositories/Mappings/EnrichedReportMappings.cs:141-155`
- Create: `TelegramGroupsAdmin.Data/Migrations/<stamp>_AddContentUserIdToEnrichedReportsView.cs` (generated)
- Test: `TelegramGroupsAdmin.IntegrationTests/ContentDetection/Repositories/EnrichedReportsViewTests.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/30_reports.sql`

**Interfaces:**
- Produces: `EnrichedReportView.ContentUserId` (`long?`, column `content_user_id`); `ReportBase.SubjectUserId` now populated for all four report types.

**Context:** `reports` has no subject-user column for ContentReport — the subject is the reported message's author. `messages` has PK `(message_id, chat_id)` (`AppDbContext.cs:105`), so the join is index-covered. `ReportBase.SubjectUserId` exists but `ToBaseModel` never assigns it, so it is always `null` today.

- [ ] **Step 1: Add three synthetic pending reports to the canonical dataset**

The golden dataset has ten reports and **all of them are already resolved** — there is no pending row to assert against, so this is hierarchy option 3 (synthetic rows whose *structural* properties matter and whose content does not). All three point at real canonical rows: message `(70989, -100054416618415)` authored by telegram user `9465377455871`, and chats `-100054416618415` / `-100048429560480`, all already present in canonical.

`reports` row count is **not** asserted by `LoadCanonicalAsyncTests` (only `messages`=407 and `welcome_responses`=11 are), and `SystemAccountBypassTests` runs on an empty template, so adding rows here contaminates nothing.

Append to `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/30_reports.sql`, **above** the existing `setval` line:

```sql
-- Synthetic pending reports for user 9465377455871 (join-gate cleanup tests).
-- Content is irrelevant; what matters structurally is: one user, three report types,
-- two chats, all status=0 (pending). The type-0 row points at real canonical message
-- (70989, -100054416618415) so the content_user_id view join resolves to 9465377455871.
INSERT INTO reports (id, message_id, chat_id, report_command_message_id, reported_by_user_id, reported_by_user_name, reported_at, status, reviewed_by, reviewed_at, action_taken, admin_notes, web_user_id, type, context) VALUES (186, 70989, -100054416618415, NULL, NULL, 'Auto-Detection', '2026-05-01 10:00:00+00', 0, NULL, NULL, NULL, NULL, NULL, 0, NULL);
INSERT INTO reports (id, message_id, chat_id, report_command_message_id, reported_by_user_id, reported_by_user_name, reported_at, status, reviewed_by, reviewed_at, action_taken, admin_notes, web_user_id, type, context) VALUES (187, 0, -100054416618415, NULL, NULL, NULL, '2026-05-01 10:01:00+00', 0, NULL, NULL, NULL, NULL, NULL, 2, '{"score": 20, "userId": 9465377455871, "mcAnswers": {"0": "A"}, "aiEvaluation": "Lorem ipsum dolor sit amet.", "shuffleState": {"0": [0, 1]}, "openEndedAnswer": "Lorem ipsum", "passingThreshold": 80}');
INSERT INTO reports (id, message_id, chat_id, report_command_message_id, reported_by_user_id, reported_by_user_name, reported_at, status, reviewed_by, reviewed_at, action_taken, admin_notes, web_user_id, type, context) VALUES (188, 0, -100048429560480, NULL, NULL, NULL, '2026-05-01 10:02:00+00', 0, NULL, NULL, NULL, NULL, NULL, 3, '{"bio": "", "score": 4.1, "isFake": false, "isScam": false, "userId": 9465377455871, "outcome": 2, "aiReason": "Lorem ipsum dolor sit amet.", "aiSignals": "Lorem ipsum", "hasPinnedStories": false, "personalChannelTitle": ""}');
```

Then change the final line from `setval('reports_id_seq', 185, true)` to:

```sql
SELECT pg_catalog.setval('reports_id_seq', 188, true);
```

- [ ] **Step 2: Write the failing integration test**

Create `TelegramGroupsAdmin.IntegrationTests/ContentDetection/Repositories/EnrichedReportsViewTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// Integration tests for the enriched_reports view.
/// The view is raw SQL, so only a real PostgreSQL round-trip proves the joins resolve.
/// </summary>
[TestFixture]
public class EnrichedReportsViewTests
{
    private const long ContentReportId = 186;
    private const long ExamReportId = 187;
    private const long ProfileReportId = 188;
    private const long SubjectUserId = 9465377455871;

    private MigrationTestHelper? _testHelper;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _testHelper?.Dispose();

    [Test]
    public async Task View_ContentReport_ResolvesContentUserIdToMessageAuthor()
    {
        await using var context = _testHelper!.GetDbContext();

        var view = await context.EnrichedReports
            .AsNoTracking()
            .SingleAsync(r => r.Id == ContentReportId);

        Assert.That(view.ContentUserId, Is.EqualTo(SubjectUserId),
            "content_user_id should resolve through messages.user_id for the reported message");
    }

    [Test]
    public async Task View_NonContentReports_LeaveContentUserIdNull()
    {
        await using var context = _testHelper!.GetDbContext();

        var views = await context.EnrichedReports
            .AsNoTracking()
            .Where(r => r.Id == ExamReportId || r.Id == ProfileReportId)
            .ToListAsync();

        Assert.That(views.Select(v => v.ContentUserId), Is.All.Null,
            "the content join is gated on type = 0");
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~EnrichedReportsViewTests"`
Expected: FAIL — `EnrichedReportView` has no `ContentUserId` member (compile error).

- [ ] **Step 4: Add the view column and join**

In `TelegramGroupsAdmin.Data/Models/EnrichedReportView.cs`, inside `CreateViewSql`, add the select item immediately after the `profile_photo_path` line and before the `-- Reviewer` comment:

```sql
            -- ContentReport: message author (type = 0)
            content_msg.user_id AS content_user_id,
```

and add the join immediately after the ProfileScanAlert join, before the reviewer join:

```sql
        -- ContentReport author (only for type = 0). Joins messages on its
        -- (message_id, chat_id) primary key. No telegram_users join — only the
        -- id is needed, for subject-user filtering.
        LEFT JOIN messages content_msg
            ON r.type = 0
            AND content_msg.chat_id = r.chat_id
            AND content_msg.message_id = r.message_id
```

Add the mapped property alongside the other per-type ids:

```csharp
    /// <summary>
    /// ContentReport (type = 0): the reported message's author, joined from messages.
    /// Null for every other report type.
    /// </summary>
    [Column("content_user_id")]
    public long? ContentUserId { get; set; }
```

- [ ] **Step 5: Populate `ReportBase.SubjectUserId`**

In `EnrichedReportMappings.ToBaseModel`, add the assignment:

```csharp
            AdminNotes = view.AdminNotes,
            SubjectUserId = (ReportType)view.Type switch
            {
                ReportType.ContentReport => view.ContentUserId,
                ReportType.ImpersonationAlert => view.SuspectedUserId,
                ReportType.ExamFailure => view.ExamUserId,
                ReportType.ProfileScanAlert => view.ProfileUserId,
                _ => null
            }
```

- [ ] **Step 6: Generate and apply the migration**

```bash
dotnet ef migrations add AddContentUserIdToEnrichedReportsView --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin
```

Then replace the generated `Up`/`Down` bodies with the drop/recreate pattern used by `20260223213357_UpdateEnrichedReportsViewForProfileScan`:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop and recreate to add content_user_id (type = 0)
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down references the code constant, which always has the latest shape.
            // Drop is safe — the original migration's Up recreates it.
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
        }
```

Add `using TelegramGroupsAdmin.Data.Models;` to the migration file.

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~EnrichedReportsViewTests"`
Expected: PASS, 2 tests.

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Data TelegramGroupsAdmin.Core/Repositories/Mappings/EnrichedReportMappings.cs TelegramGroupsAdmin.IntegrationTests
git commit -F- <<'EOF'
feat(reports): expose the content report subject user on enriched_reports

Adds a content_user_id column joined from messages on (message_id, chat_id)
so ContentReport has a queryable subject user like the other three types,
and populates ReportBase.SubjectUserId, which ToBaseModel never assigned.

Needed by the ban-closes-open-reports rule, which must find every pending
report for a user regardless of type.
EOF
```

---

### Task 2: `ReportCleanupHandler` worker

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Repositories/IReportsRepository.cs`
- Modify: `TelegramGroupsAdmin.Core/Repositories/ReportsRepository.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/Moderation/Actions/IReportCleanupHandler.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/Moderation/Actions/ReportCleanupHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs:120-122`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Actions/ReportCleanupHandlerTests.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/ContentDetection/Repositories/ReportsRepositoryTests.cs`

**Interfaces:**
- Consumes: `ReportBase.SubjectUserId`, `EnrichedReportView.ContentUserId` (Task 1).
- Produces: `IReportsRepository.GetPendingForUserAsync(long userId, long? chatId = null, CancellationToken)` → `Task<List<ReportBase>>`; `IReportCleanupHandler.CloseOpenReportsAsync(UserIdentity user, ChatIdentity? chat, Actor executor, string actionName, long? excludeReportId, CancellationToken)` → `Task<int>`.

- [ ] **Step 1: Write the failing repository integration test**

Append to `TelegramGroupsAdmin.IntegrationTests/ContentDetection/Repositories/ReportsRepositoryTests.cs`. This fixture currently builds an empty-template database in `SetUp`; add a separate canonical-backed fixture in the same file so the golden rows are available:

```csharp
/// <summary>
/// Canonical-dataset tests for cross-type pending lookups.
/// Uses the golden template because the assertion is about the view's four
/// subject-user joins resolving against real report/message/user rows.
/// </summary>
[TestFixture]
public class ReportsRepositoryPendingForUserTests
{
    private const long SubjectUserId = 9465377455871;
    private const long ChatWithTwoReports = -100054416618415;

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IReportsRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IReportsRepository, ReportsRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IReportsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    [Test]
    public async Task GetPendingForUserAsync_NoChatFilter_ReturnsEveryTypeAcrossChats()
    {
        var pending = await _repository!.GetPendingForUserAsync(SubjectUserId);

        Assert.That(pending.Select(r => r.Type), Is.EquivalentTo(new[]
        {
            ReportType.ContentReport,
            ReportType.ExamFailure,
            ReportType.ProfileScanAlert
        }));
    }

    [Test]
    public async Task GetPendingForUserAsync_WithChatFilter_ReturnsOnlyThatChat()
    {
        var pending = await _repository!.GetPendingForUserAsync(SubjectUserId, ChatWithTwoReports);

        Assert.That(pending.Select(r => r.Chat.Id), Is.All.EqualTo(ChatWithTwoReports));
        Assert.That(pending, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetPendingForUserAsync_ExcludesAlreadyReviewedReports()
    {
        var pending = await _repository!.GetPendingForUserAsync(SubjectUserId);

        Assert.That(pending.Select(r => r.Status), Is.All.EqualTo(ReportStatus.Pending));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~ReportsRepositoryPendingForUserTests"`
Expected: FAIL — `GetPendingForUserAsync` is not defined.

- [ ] **Step 3: Add the repository method**

In `IReportsRepository.cs`, under the generic operations region (next to `GetPendingAsync`):

```csharp
    /// <summary>
    /// Get every pending report whose subject is the given user, regardless of report type.
    /// Subject resolution per type: ContentReport → reported message author,
    /// ImpersonationAlert → suspected user, ExamFailure / ProfileScanAlert → the user.
    /// </summary>
    /// <param name="userId">Telegram user id of the report subject.</param>
    /// <param name="chatId">When supplied, narrows to reports in that chat.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<ReportBase>> GetPendingForUserAsync(
        long userId,
        long? chatId = null,
        CancellationToken cancellationToken = default);
```

In `ReportsRepository.cs`, after `GetPendingAsync`:

```csharp
    public async Task<List<ReportBase>> GetPendingForUserAsync(
        long userId,
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.EnrichedReports
            .AsNoTracking()
            .Where(r => r.Status == (int)ReportStatus.Pending)
            .Where(r => r.ContentUserId == userId
                        || r.SuspectedUserId == userId
                        || r.ExamUserId == userId
                        || r.ProfileUserId == userId);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        var views = await query
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        return views.Select(v => v.ToBaseModel()).ToList();
    }
```

- [ ] **Step 4: Run the integration test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~ReportsRepositoryPendingForUserTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Write the failing worker unit test**

Create `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Actions/ReportCleanupHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for ReportCleanupHandler.
/// The worker owns no policy — it closes what the orchestrator hands it and nothing else.
/// </summary>
[TestFixture]
public class ReportCleanupHandlerTests
{
    private static readonly UserIdentity TestUser = new(555L, "Test", null, "testuser");
    private static readonly ChatIdentity TestChat = new(-100123L, "TestChat");
    private static readonly Actor TestExecutor = Actor.AutoDetection;

    private IReportsRepository _reportsRepository = null!;
    private ReportCleanupHandler _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _reportsRepository = Substitute.For<IReportsRepository>();
        _sut = new ReportCleanupHandler(
            _reportsRepository,
            Substitute.For<ILogger<ReportCleanupHandler>>());
    }

    private static ReportBase Report(long id, ReportType type) => new()
    {
        Id = id,
        Type = type,
        Chat = TestChat,
        Status = ReportStatus.Pending
    };

    [Test]
    public async Task CloseOpenReportsAsync_ClosesEveryPendingReport()
    {
        _reportsRepository
            .GetPendingForUserAsync(TestUser.Id, null, Arg.Any<CancellationToken>())
            .Returns([Report(1, ReportType.ExamFailure), Report(2, ReportType.ProfileScanAlert)]);
        _reportsRepository
            .TryUpdateStatusAsync(Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var closed = await _sut.CloseOpenReportsAsync(
            TestUser, chat: null, TestExecutor, "Ban", excludeReportId: null);

        Assert.That(closed, Is.EqualTo(2));
        await _reportsRepository.Received(1).TryUpdateStatusAsync(
            1, ReportStatus.Reviewed, Arg.Any<string>(), "Auto-Ban", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _reportsRepository.Received(1).TryUpdateStatusAsync(
            2, ReportStatus.Reviewed, Arg.Any<string>(), "Auto-Ban", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CloseOpenReportsAsync_SkipsTheOriginatingReport()
    {
        _reportsRepository
            .GetPendingForUserAsync(TestUser.Id, null, Arg.Any<CancellationToken>())
            .Returns([Report(1, ReportType.ProfileScanAlert), Report(2, ReportType.ExamFailure)]);
        _reportsRepository
            .TryUpdateStatusAsync(Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var closed = await _sut.CloseOpenReportsAsync(
            TestUser, chat: null, TestExecutor, "Ban", excludeReportId: 1);

        Assert.That(closed, Is.EqualTo(1));
        await _reportsRepository.DidNotReceive().TryUpdateStatusAsync(
            1, Arg.Any<ReportStatus>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CloseOpenReportsAsync_LostRaceIsNotCounted()
    {
        _reportsRepository
            .GetPendingForUserAsync(TestUser.Id, null, Arg.Any<CancellationToken>())
            .Returns([Report(1, ReportType.ExamFailure)]);
        _reportsRepository
            .TryUpdateStatusAsync(Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var closed = await _sut.CloseOpenReportsAsync(
            TestUser, chat: null, TestExecutor, "Ban", excludeReportId: null);

        Assert.That(closed, Is.Zero, "an admin who won the race keeps ownership of the row");
    }

    [Test]
    public async Task CloseOpenReportsAsync_WithChat_ScopesTheLookup()
    {
        _reportsRepository
            .GetPendingForUserAsync(TestUser.Id, TestChat.Id, Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.CloseOpenReportsAsync(
            TestUser, TestChat, TestExecutor, "Kick", excludeReportId: null);

        await _reportsRepository.Received(1)
            .GetPendingForUserAsync(TestUser.Id, TestChat.Id, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ReportCleanupHandlerTests"`
Expected: FAIL — `ReportCleanupHandler` does not exist (compile error).

- [ ] **Step 7: Write the worker**

Create `IReportCleanupHandler.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for closing a user's still-open reports after a moderation action.
/// Owns no policy: the orchestrator decides when to call it and at what scope.
/// Does NOT know about bans, welcome flows, or notifications.
/// </summary>
public interface IReportCleanupHandler
{
    /// <summary>
    /// Close every pending report whose subject is <paramref name="user"/>.
    /// </summary>
    /// <param name="user">The report subject.</param>
    /// <param name="chat">Null closes reports in every chat; a value narrows to that chat.</param>
    /// <param name="executor">Recorded as the reviewer on each closed report.</param>
    /// <param name="actionName">Action label, e.g. "Ban" or "Kick". Stored as "Auto-{actionName}".</param>
    /// <param name="excludeReportId">
    /// Report that triggered the action, if any. It is skipped so the calling handler keeps
    /// ownership of its own status update and does not lose the race to this cleanup.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of reports actually closed.</returns>
    Task<int> CloseOpenReportsAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        string actionName,
        long? excludeReportId,
        CancellationToken cancellationToken = default);
}
```

Create `ReportCleanupHandler.cs`:

```csharp
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <inheritdoc />
public sealed class ReportCleanupHandler(
    IReportsRepository reportsRepository,
    ILogger<ReportCleanupHandler> logger) : IReportCleanupHandler
{
    public async Task<int> CloseOpenReportsAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        string actionName,
        long? excludeReportId,
        CancellationToken cancellationToken = default)
    {
        var pending = await reportsRepository.GetPendingForUserAsync(user.Id, chat?.Id, cancellationToken);
        if (pending.Count == 0)
            return 0;

        // Plain chat id, not ToLogInfo() — this string is persisted in admin_notes,
        // so it must not carry a log-display format that can change underneath it.
        var scope = chat is null ? "globally" : $"in chat {chat.Id}";
        var note = $"Auto-resolved: user {actionName.ToLowerInvariant()}ed {scope}";

        var closed = 0;
        foreach (var report in pending)
        {
            if (excludeReportId.HasValue && report.Id == excludeReportId.Value)
                continue;

            // TryUpdateStatusAsync is atomic-on-pending: if an admin resolved this row
            // between the read and here, they win and we leave their decision alone.
            var updated = await reportsRepository.TryUpdateStatusAsync(
                report.Id,
                ReportStatus.Reviewed,
                executor.GetDisplayText(),
                $"Auto-{actionName}",
                note,
                cancellationToken);

            if (!updated)
                continue;

            closed++;
            logger.LogDebug("Auto-closed {ReportType} report #{ReportId} for {User}",
                report.Type, report.Id, user.ToLogDebug());
        }

        if (closed > 0)
        {
            logger.LogInformation(
                "Report cleanup: auto-closed {Count} open report(s) for {User} after {Action}",
                closed, user.ToLogInfo(), actionName);
        }

        return closed;
    }
}
```

`"Ban"` → `"banned"` and `"Kick"` → `"kicked"` both come out right from `{actionName.ToLowerInvariant()}ed`.

- [ ] **Step 8: Register it in DI**

In `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`, in the "Moderation domain handlers" block:

```csharp
            services.AddScoped<ITrustHandler, TrustHandler>();
            services.AddScoped<IWarnHandler, WarnHandler>();
            services.AddScoped<IReportCleanupHandler, ReportCleanupHandler>();
```

- [ ] **Step 9: Run the unit tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ReportCleanupHandlerTests"`
Expected: PASS, 4 tests.

- [ ] **Step 10: Commit**

```bash
git add TelegramGroupsAdmin.Core TelegramGroupsAdmin.Telegram TelegramGroupsAdmin.UnitTests TelegramGroupsAdmin.IntegrationTests
git commit -F- <<'EOF'
feat(moderation): add ReportCleanupHandler worker

Closes every pending report for a user, at global or per-chat scope, skipping
the report that triggered the action so the calling handler keeps ownership of
its own status update.

Backed by a new IReportsRepository.GetPendingForUserAsync that matches any of
the four subject-user columns on enriched_reports.
EOF
```

---

### Task 3: `WelcomeCleanupHandler` worker

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IWelcomeResponsesRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/WelcomeResponsesRepository.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/Moderation/Actions/IWelcomeCleanupHandler.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/Moderation/Actions/WelcomeCleanupHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Actions/WelcomeCleanupHandlerTests.cs`

**Interfaces:**
- Consumes: `IBotModerationMessageHandler.DeleteAsync(ChatIdentity chat, int messageId, Actor executor, CancellationToken)` → `Task<DeleteResult>` (existing).
- Produces: `IWelcomeResponsesRepository.GetByUserAsync(long userId, CancellationToken)` → `Task<List<WelcomeResponse>>`; `IWelcomeCleanupHandler.DeleteStrandedWelcomeMessagesAsync(UserIdentity user, ChatIdentity? chat, Actor executor, CancellationToken)` → `Task<int>`.

**Context:** The worker deliberately does **not** write `welcome_responses.response`. The final state (`Denied` / `Timeout` / `Left`) is owned by the caller, and callers write it *after* calling the boss — a write here would clobber `WelcomeTimeoutJob`'s more accurate `Timeout`.

- [ ] **Step 1: Write the failing unit test**

Create `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Actions/WelcomeCleanupHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for WelcomeCleanupHandler.
/// Deletes the stranded welcome/teaser message; never touches the response state,
/// which each caller owns.
/// </summary>
[TestFixture]
public class WelcomeCleanupHandlerTests
{
    private const long ChatAId = -100111L;
    private const long ChatBId = -100222L;
    private static readonly UserIdentity TestUser = new(555L, "Test", null, "testuser");
    private static readonly Actor TestExecutor = Actor.AutoDetection;

    private IWelcomeResponsesRepository _welcomeRepository = null!;
    private IBotModerationMessageHandler _messageHandler = null!;
    private WelcomeCleanupHandler _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _welcomeRepository = Substitute.For<IWelcomeResponsesRepository>();
        _messageHandler = Substitute.For<IBotModerationMessageHandler>();
        _messageHandler
            .DeleteAsync(Arg.Any<ChatIdentity>(), Arg.Any<int>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded());

        _sut = new WelcomeCleanupHandler(
            _welcomeRepository,
            _messageHandler,
            Substitute.For<ILogger<WelcomeCleanupHandler>>());
    }

    private static WelcomeResponse Response(long chatId, int welcomeMessageId) => new(
        Id: 1,
        ChatId: chatId,
        UserId: TestUser.Id,
        Username: "testuser",
        WelcomeMessageId: welcomeMessageId,
        Response: WelcomeResponseType.Accepted,
        RespondedAt: DateTimeOffset.UtcNow,
        DmSent: false,
        DmFallback: false,
        CreatedAt: DateTimeOffset.UtcNow,
        TimeoutJobId: null);

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_NoChat_DeletesAcrossEveryChat()
    {
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 100), Response(ChatBId, 200)]);

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        Assert.That(deleted, Is.EqualTo(2));
        await _messageHandler.Received(1).DeleteAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatAId), 100, TestExecutor, Arg.Any<CancellationToken>());
        await _messageHandler.Received(1).DeleteAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatBId), 200, TestExecutor, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_WithChat_DeletesOnlyThatChat()
    {
        _welcomeRepository.GetByUserAndChatAsync(TestUser.Id, ChatAId, Arg.Any<CancellationToken>())
            .Returns(Response(ChatAId, 100));

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(
            TestUser, new ChatIdentity(ChatAId, "ChatA"), TestExecutor);

        Assert.That(deleted, Is.EqualTo(1));
        await _welcomeRepository.DidNotReceive().GetByUserAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_NeverWritesResponseState()
    {
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 100)]);

        await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        await _welcomeRepository.DidNotReceive().UpdateResponseAsync(
            Arg.Any<long>(), Arg.Any<WelcomeResponseType>(), Arg.Any<bool>(), Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_SkipsZeroMessageId()
    {
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 0)]);

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        Assert.That(deleted, Is.Zero);
        await _messageHandler.DidNotReceive().DeleteAsync(
            Arg.Any<ChatIdentity>(), Arg.Any<int>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_DeleteFailureDoesNotThrow()
    {
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 100)]);
        _messageHandler
            .DeleteAsync(Arg.Any<ChatIdentity>(), Arg.Any<int>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns<DeleteResult>(_ => throw new InvalidOperationException("message already gone"));

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        Assert.That(deleted, Is.Zero, "cleanup must never fail the ban that already landed");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeCleanupHandlerTests"`
Expected: FAIL — `WelcomeCleanupHandler` and `GetByUserAsync` do not exist.

- [ ] **Step 3: Add the repository method**

In `IWelcomeResponsesRepository.cs`:

```csharp
    /// <summary>
    /// Get the most recent welcome response per chat for a user, across every chat.
    /// Used by ban cleanup, which is global and has no single chat to scope to.
    /// </summary>
    Task<List<WelcomeResponse>> GetByUserAsync(long userId, CancellationToken cancellationToken = default);
```

In `WelcomeResponsesRepository.cs`, after `GetByUserAndChatAsync`:

```csharp
    public async Task<List<WelcomeResponse>> GetByUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // One row per chat — the newest, matching GetByUserAndChatAsync's ordering.
        var entities = await context.WelcomeResponses
            .AsNoTracking()
            .Where(wr => wr.UserId == userId)
            .GroupBy(wr => wr.ChatId)
            .Select(g => g.OrderByDescending(wr => wr.CreatedAt).First())
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }
```

- [ ] **Step 4: Write the worker**

Create `IWelcomeCleanupHandler.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for removing a user's leftover welcome/exam teaser message from chat.
///
/// The welcome flow reuses one message for its whole lifecycle: the "Verifying..." post is
/// edited into the welcome or exam teaser, and welcome_responses.welcome_message_id is the
/// only handle anyone has for deleting it. WelcomeTimeoutJob is the only unconditional
/// cleaner, and it is cancelled as soon as the user responds — so a user who responded and
/// was then banned during admin review leaves that message stranded in the chat.
///
/// Deliberately does NOT write welcome_responses.response: the final state
/// (Denied / Timeout / Left) belongs to the caller, which writes it after the moderation call.
/// </summary>
public interface IWelcomeCleanupHandler
{
    /// <summary>
    /// Delete the user's welcome message in one chat, or in every chat when
    /// <paramref name="chat"/> is null (global ban).
    /// </summary>
    /// <returns>Number of messages actually deleted.</returns>
    Task<int> DeleteStrandedWelcomeMessagesAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        CancellationToken cancellationToken = default);
}
```

Create `WelcomeCleanupHandler.cs`:

```csharp
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <inheritdoc />
public sealed class WelcomeCleanupHandler(
    IWelcomeResponsesRepository welcomeResponsesRepository,
    IBotModerationMessageHandler messageHandler,
    ILogger<WelcomeCleanupHandler> logger) : IWelcomeCleanupHandler
{
    public async Task<int> DeleteStrandedWelcomeMessagesAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        CancellationToken cancellationToken = default)
    {
        List<WelcomeResponse> responses;
        if (chat is null)
        {
            responses = await welcomeResponsesRepository.GetByUserAsync(user.Id, cancellationToken);
        }
        else
        {
            var single = await welcomeResponsesRepository.GetByUserAndChatAsync(
                user.Id, chat.Id, cancellationToken);
            responses = single is null ? [] : [single];
        }

        var deleted = 0;
        foreach (var response in responses)
        {
            if (response.WelcomeMessageId == 0)
                continue;

            var targetChat = chat ?? ChatIdentity.FromId(response.ChatId);

            try
            {
                // Idempotent: deleting an already-deleted message is a no-op at the API level.
                await messageHandler.DeleteAsync(
                    targetChat, response.WelcomeMessageId, executor, cancellationToken);

                deleted++;
                logger.LogDebug("Deleted stranded welcome message {MessageId} for {User} in {Chat}",
                    response.WelcomeMessageId, user.ToLogDebug(), targetChat.ToLogDebug());
            }
            catch (Exception ex)
            {
                // Cleanup must never fail a ban that already landed on Telegram.
                logger.LogDebug(ex,
                    "Failed to delete stranded welcome message {MessageId} for {User} in {Chat} (non-fatal)",
                    response.WelcomeMessageId, user.ToLogDebug(), targetChat.ToLogDebug());
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation("Welcome cleanup: deleted {Count} stranded welcome message(s) for {User}",
                deleted, user.ToLogInfo());
        }

        return deleted;
    }
}
```

- [ ] **Step 5: Register it in DI**

```csharp
            services.AddScoped<IReportCleanupHandler, ReportCleanupHandler>();
            services.AddScoped<IWelcomeCleanupHandler, WelcomeCleanupHandler>();
```

- [ ] **Step 6: Run the unit tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeCleanupHandlerTests"`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Telegram TelegramGroupsAdmin.UnitTests
git commit -F- <<'EOF'
feat(moderation): add WelcomeCleanupHandler worker

Deletes the welcome/exam teaser message a banned or kicked user leaves behind.
WelcomeTimeoutJob is cancelled as soon as the user responds, so a user who
accepted and was then banned during admin review had no cleanup path at all.

Does not write welcome_responses.response — the final state belongs to the
caller, which writes it after the moderation call.
EOF
```

---

### Task 4: Boss wiring — a ban/kick closes reports and clears the welcome message

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Intents/ModerationIntent.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotModerationService.cs:32-88` (fields + ctor), `:179-227` (`BanUserAsync`), `:518-578` (`KickUserFromChatAsync`)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/BotModerationServiceTests.cs`

**Interfaces:**
- Consumes: `IReportCleanupHandler` (Task 2), `IWelcomeCleanupHandler` (Task 3).
- Produces: `ModerationIntent.OriginReportId` (`long?`), inherited by `BanIntent` and `KickIntent`.

- [ ] **Step 1: Write the failing orchestrator tests**

Append to `BotModerationServiceTests.cs`, and add the two new fields/mocks to the existing `SetUp` (see Step 3):

```csharp
    #region Report + Welcome Cleanup Rules

    [Test]
    public async Task BanUserAsync_ClosesOpenReportsGlobally()
    {
        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 3, chatsFailed: 0));

        var user = new UserIdentity(555L, "Test", null, "testuser");
        await _orchestrator.BanUserAsync(new BanIntent
        {
            User = user,
            Executor = Actor.AutoDetection,
            Reason = "test",
            OriginReportId = 42
        });

        await _mockReportCleanupHandler.Received(1).CloseOpenReportsAsync(
            user, null, Arg.Any<Actor>(), "Ban", 42, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanUserAsync_DeletesStrandedWelcomeMessagesGlobally()
    {
        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 1, chatsFailed: 0));

        var user = new UserIdentity(555L, "Test", null, "testuser");
        await _orchestrator.BanUserAsync(new BanIntent
        {
            User = user,
            Executor = Actor.AutoDetection,
            Reason = "test"
        });

        await _mockWelcomeCleanupHandler.Received(1).DeleteStrandedWelcomeMessagesAsync(
            user, null, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanUserAsync_FailedBan_SkipsCleanup()
    {
        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Failed("API error"));

        await _orchestrator.BanUserAsync(new BanIntent
        {
            User = new UserIdentity(555L, "Test", null, "testuser"),
            Executor = Actor.AutoDetection,
            Reason = "test"
        });

        await _mockReportCleanupHandler.DidNotReceive().CloseOpenReportsAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
            Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        await _mockWelcomeCleanupHandler.DidNotReceive().DeleteStrandedWelcomeMessagesAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanUserAsync_CleanupThrows_BanStillSucceeds()
    {
        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 1, chatsFailed: 0));
        _mockReportCleanupHandler.CloseOpenReportsAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _orchestrator.BanUserAsync(new BanIntent
        {
            User = new UserIdentity(555L, "Test", null, "testuser"),
            Executor = Actor.AutoDetection,
            Reason = "test"
        });

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task KickUserFromChatAsync_ScopesCleanupToThatChat()
    {
        _mockConfigService.GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new WelcomeConfig { MaxKicksBeforeBan = 0 });
        _mockBanHandler.KickFromChatAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(),
                Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<KickOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 1, chatsFailed: 0));

        var user = new UserIdentity(555L, "Test", null, "testuser");
        var chat = new ChatIdentity(TestChatId, "TestChat");

        await _orchestrator.KickUserFromChatAsync(new KickIntent
        {
            User = user,
            Chat = chat,
            Executor = Actor.WelcomeFlow,
            Reason = "test",
            OriginReportId = 7
        });

        await _mockReportCleanupHandler.Received(1).CloseOpenReportsAsync(
            user, chat, Arg.Any<Actor>(), "Kick", 7, Arg.Any<CancellationToken>());
        await _mockWelcomeCleanupHandler.Received(1).DeleteStrandedWelcomeMessagesAsync(
            user, chat, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    #endregion
```

`BanResult.Succeeded` / `BanResult.Failed` and `new WelcomeConfig { MaxKicksBeforeBan = 0 }` match the conventions already used throughout this fixture. `ThrowsAsync` is available via the file's existing `using NSubstitute.ExceptionExtensions;`.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BotModerationServiceTests"`
Expected: FAIL — `OriginReportId` and the two mock fields do not exist (compile error).

- [ ] **Step 3: Add `OriginReportId` to the base intent**

In `TelegramGroupsAdmin.Telegram/Services/Moderation/Intents/ModerationIntent.cs`, add to the abstract record:

```csharp
    /// <summary>
    /// Report id that triggered this action, when one did. The orchestrator's report-cleanup
    /// rule skips it, so the report handler that initiated the action keeps ownership of its
    /// own status update instead of losing the race to the auto-close sweep.
    /// </summary>
    public long? OriginReportId { get; init; }
```

- [ ] **Step 4: Wire the two workers into the orchestrator**

In `BotModerationService.cs`, add the fields next to the other domain handlers:

```csharp
    private readonly IReportCleanupHandler _reportCleanupHandler;
    private readonly IWelcomeCleanupHandler _welcomeCleanupHandler;
```

Add the constructor parameters after `IBotRestrictHandler restrictHandler` and assign them:

```csharp
        IBotRestrictHandler restrictHandler,
        IReportCleanupHandler reportCleanupHandler,
        IWelcomeCleanupHandler welcomeCleanupHandler,
        IAuditHandler auditHandler,
```

```csharp
        _reportCleanupHandler = reportCleanupHandler;
        _welcomeCleanupHandler = welcomeCleanupHandler;
```

In `BanUserAsync`, immediately after the `ScheduleUserMessagesCleanupAsync` block and before the ban-celebration block:

```csharp
        // Business rule: a ban resolves every open report about this user, everywhere.
        // OriginReportId is skipped so the report handler that started this keeps
        // ownership of its own status update.
        await SafeExecuteAsync(
            () => _reportCleanupHandler.CloseOpenReportsAsync(
                intent.User, chat: null, intent.Executor, "Ban", intent.OriginReportId, cancellationToken),
            $"Close open reports for user {intent.User.Id}");

        // Business rule: a ban ends any pending welcome, so the welcome/teaser message
        // must not be left sitting in chat with live buttons.
        await SafeExecuteAsync(
            () => _welcomeCleanupHandler.DeleteStrandedWelcomeMessagesAsync(
                intent.User, chat: null, intent.Executor, cancellationToken),
            $"Delete stranded welcome messages for user {intent.User.Id}");
```

In `KickUserFromChatAsync`, after the `IncrementKickCountAsync` block and before the `return`:

```csharp
        // Same rules as ban, narrowed to this chat — a kick is a statement about one chat.
        await SafeExecuteAsync(
            () => _reportCleanupHandler.CloseOpenReportsAsync(
                intent.User, intent.Chat, intent.Executor, "Kick", intent.OriginReportId, cancellationToken),
            $"Close open reports for user {intent.User.Id} in chat {intent.Chat.Id}");

        await SafeExecuteAsync(
            () => _welcomeCleanupHandler.DeleteStrandedWelcomeMessagesAsync(
                intent.User, intent.Chat, intent.Executor, cancellationToken),
            $"Delete stranded welcome message for user {intent.User.Id} in chat {intent.Chat.Id}");
```

`SafeExecuteAsync` takes a `Func<Task>`; both worker calls return `Task<int>`, which is assignable to `Task`, so no wrapper lambda body is needed.

The kick-escalation branch (`priorKickCount >= maxKicks`) returns `BanUserAsync(...)` before reaching this code, so cleanup runs exactly once at ban scope. Thread the origin through that escalation too:

```csharp
                return await BanUserAsync(new BanIntent
                {
                    User = intent.User,
                    Chat = intent.Chat,
                    Executor = intent.Executor,
                    Reason = $"Auto-ban: {priorKickCount} prior kicks (threshold: {maxKicks})",
                    OriginReportId = intent.OriginReportId
                }, cancellationToken);
```

- [ ] **Step 5: Update the existing test fixture's `SetUp`**

Add the two mocks and pass them in the constructor call, in the same positions as the real constructor:

```csharp
    private IReportCleanupHandler _mockReportCleanupHandler = null!;
    private IWelcomeCleanupHandler _mockWelcomeCleanupHandler = null!;
```

```csharp
        _mockReportCleanupHandler = Substitute.For<IReportCleanupHandler>();
        _mockWelcomeCleanupHandler = Substitute.For<IWelcomeCleanupHandler>();
```

```csharp
            _mockRestrictHandler,
            _mockReportCleanupHandler,
            _mockWelcomeCleanupHandler,
            _mockAuditHandler,
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BotModerationServiceTests"`
Expected: PASS — all pre-existing tests plus the 5 new ones.

- [ ] **Step 7: Run the integration suite**

`BotModerationService` gained two constructor dependencies, so DI resolution must be verified against the real container, not just mocks.

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Telegram TelegramGroupsAdmin.UnitTests
git commit -F- <<'EOF'
fix(moderation): a ban or kick now closes open reports and clears the welcome message

Two cross-cutting rules move onto the orchestrator, each executed by a worker:
a ban closes every pending report for the user across all chats and deletes
their stranded welcome messages; a kick does the same scoped to one chat.

ModerationIntent gains OriginReportId so the report handler that triggered the
action is skipped by the sweep and keeps ownership of its own status update.
EOF
```

---

### Task 5: Report handlers and welcome callers stop doing the boss's job

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/IExamFlowService.cs:132-152`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ExamFlowService.cs:826-900`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs:1112-1167`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Jobs/WelcomeTimeoutJob.cs:128-152`
- Test: `TelegramGroupsAdmin.UnitTests/Services/ReportActions/ProfileScanHandlerTests.cs`

**Interfaces:**
- Consumes: `ModerationIntent.OriginReportId` (Task 4).
- Produces: `IExamFlowService.DenyExamFailureAsync` / `DenyAndBanExamFailureAsync` gain a `long? originReportId` parameter, placed after `executor`.

- [ ] **Step 1: Update the ProfileScanHandler tests to the new contract**

In `TelegramGroupsAdmin.UnitTests/Services/ReportActions/ProfileScanHandlerTests.cs`, replace assertions that `BanAsync`/`KickAsync` auto-close sibling alerts with assertions that the origin id is handed to the orchestrator instead:

```csharp
    #region Orchestrator Ownership Tests

    [Test]
    public async Task BanAsync_PassesOriginReportIdToOrchestrator()
    {
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(CreateTestAlert());
        _mockModerationService.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockModerationService.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i => i!.OriginReportId == TestAlertId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task KickAsync_PassesOriginReportIdToOrchestrator()
    {
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(CreateTestAlert());
        _mockModerationService.KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        await _handler.KickAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockModerationService.Received(1).KickUserFromChatAsync(
            Arg.Is<KickIntent>(i => i!.OriginReportId == TestAlertId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanAsync_DoesNotCloseSiblingAlertsItself()
    {
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(CreateTestAlert());
        _mockModerationService.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockReportsRepo.DidNotReceive().GetPendingProfileScanAlertsForUserAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AllowAsync_StillClosesSiblingProfileScanAlerts()
    {
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(CreateTestAlert());
        _mockWelcomeRepo.GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((WelcomeResponse?)null);
        _mockAdmissionHandler.TryAdmitUserAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AdmissionResult.Admitted);

        await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockReportsRepo.Received(1).GetPendingProfileScanAlertsForUserAsync(
            TestUserId, Arg.Any<CancellationToken>());
    }

    #endregion
```

`CreateTestAlert()` and the `_mock*` field names are the fixture's existing helpers — reuse them as-is.
Delete any existing test in this file that asserts `BanAsync` or `KickAsync` closes sibling alerts;
those assertions now belong to `BotModerationServiceTests` (Task 4).

- [ ] **Step 2: Run to verify the new tests fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScanHandlerTests"`
Expected: FAIL — `OriginReportId` is not set, and ban still calls `GetPendingProfileScanAlertsForUserAsync`.

- [ ] **Step 3: Narrow `ProfileScanHandler`**

Set the origin on both intents:

```csharp
        var result = await moderationService.BanUserAsync(
            new BanIntent
            {
                User = alert.User,
                Executor = executor,
                Reason = $"Profile scan alert #{alertId} confirmed — score {alert.Score:F1}",
                Chat = alert.Chat,
                OriginReportId = alertId
            },
            cancellationToken);
```

```csharp
            var result = await moderationService.KickUserFromChatAsync(
                new KickIntent
                {
                    User = alert.User,
                    Chat = alert.Chat,
                    Executor = executor,
                    Reason = $"Profile scan alert #{alertId} — kicked after review",
                    RevokeMessages = false,
                    OriginReportId = alertId
                },
                cancellationToken);
```

Delete the `await CleanupSiblingAlertsAsync(alert, "Ban", cancellationToken);` line from `BanAsync` and the `"Kick"` one from `KickAsync`. **Keep** the call in `AllowAsync` — Allow performs no moderation action, so the orchestrator never runs, and Allow is deliberately narrower than a ban: it closes duplicate observations of the same profile only, never a pending exam failure or content report.

Update the `CleanupSiblingAlertsAsync` XML comment and the class summary to say so:

```csharp
/// <summary>
/// Handles profile scan alert actions (ban, kick, allow).
/// Fetches alert, executes moderation, and atomically updates status.
/// Ban and kick cleanup (closing the user's other open reports, deleting the stranded
/// welcome message) is owned by BotModerationService — this handler only supplies
/// OriginReportId so its own alert is excluded from that sweep. Allow performs no
/// moderation action, so it still closes its own sibling profile scan alerts.
/// </summary>
```

```csharp
    /// <summary>
    /// Close the user's other pending profile scan alerts after an Allow.
    /// Deliberately scoped to profile scan alerts only: an admin allowing a profile scan is a
    /// weaker signal than a ban and must not auto-dismiss a pending exam failure or content report.
    /// Ban and kick do not call this — BotModerationService closes all report types for them.
    /// </summary>
```

- [ ] **Step 4: Thread the origin id through the exam denial path**

In `IExamFlowService.cs`, add the parameter to both denial methods and document it:

```csharp
    /// <param name="originReportId">
    /// Exam failure report id that triggered this denial. Passed to the moderation
    /// orchestrator so its report-cleanup sweep skips this report.
    /// </param>
    Task<ModerationResult> DenyExamFailureAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        long? originReportId = null,
        CancellationToken cancellationToken = default);
```

```csharp
    Task<ModerationResult> DenyAndBanExamFailureAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        long? originReportId = null,
        CancellationToken cancellationToken = default);
```

Also update these methods' summary lines: they no longer say "deletes teaser message" for the denial path (the orchestrator does that now).

In `ExamFlowService.cs`, mirror the parameter on both public methods and on `ExecuteExamDenialAsync`, and set it on both intents:

```csharp
            var banResult = await orchestrator.BanUserAsync(
                new BanIntent
                {
                    User = user,
                    Executor = executor,
                    Reason = "Exam failed - banned to prevent repeat join spam",
                    OriginReportId = originReportId
                },
                cancellationToken);
```

```csharp
            var kickResult = await orchestrator.KickUserFromChatAsync(
                new KickIntent
                {
                    User = user,
                    Chat = chat,
                    Executor = executor,
                    Reason = "Exam denied - kicked from chat",
                    OriginReportId = originReportId
                },
                cancellationToken);
```

In `ExecuteExamDenialAsync`, delete the `orchestrator.DeleteMessageAsync(new DeleteMessageIntent { ... "Exam teaser cleanup after denial" ... })` call — the orchestrator's welcome-cleanup rule covers it once the kick/ban lands. **Keep** the `UpdateResponseAsync(welcomeResponse.Id, WelcomeResponseType.Denied, ...)` call; the state write still belongs here. The `if (welcomeResponse != null)` block reduces to just that update.

**Do not touch** `ExecuteExamApprovalAsync` — approval involves no ban or kick, so its own teaser delete is the only cleanup that path has.

In `ExamHandler.cs`, pass the exam id through:

```csharp
        var result = await examFlowService.DenyExamFailureAsync(
            exam.User, exam.Chat, executor, examId, cancellationToken);
```

```csharp
        var result = await examFlowService.DenyAndBanExamFailureAsync(
            exam.User, exam.Chat, executor, examId, cancellationToken);
```

- [ ] **Step 5: Drop the now-duplicated deletes in the welcome paths**

In `WelcomeService.HandleDenyAsync`, delete step 3 and renumber the comments:

```csharp
        // Step 2: Kick user (the moderation orchestrator deletes the welcome message)
        await KickUserAsync(chat, user, ReasonDeniedRules, cancellationToken);

        // Step 3: Update or create response record
```

In `WelcomeTimeoutJob.ExecuteAsync`, delete the post-kick "Delete welcome message" try/catch block (the one with `deletionSource: "welcome_timeout"`), replacing it with a comment:

```csharp
            // The moderation orchestrator deletes the welcome message as part of the kick.
```

**Do not touch** the earlier already-handled branch (`deletionSource: "welcome_timeout_cleanup"`) — that path returns without kicking, so it is the only cleaner there.

- [ ] **Step 6: Run the affected unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScanHandlerTests|FullyQualifiedName~ExamHandlerTests|FullyQualifiedName~WelcomeService"`
Expected: PASS. Fix any fixture that constructs `DenyExamFailureAsync` mocks with the old arity.

- [ ] **Step 7: Run the full unit suite and build**

Run: `dotnet build TelegramGroupsAdmin.sln` then `dotnet test TelegramGroupsAdmin.UnitTests`
Expected: build clean (no new warnings), all tests PASS.

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Telegram TelegramGroupsAdmin.BackgroundJobs TelegramGroupsAdmin.UnitTests
git commit -F- <<'EOF'
refactor(reports): report handlers stop doing the orchestrator's cleanup

ProfileScanHandler no longer auto-closes sibling alerts on ban/kick and the
exam denial path no longer deletes the teaser — the orchestrator owns both now,
and does them for every report type rather than just profile scans. Each handler
passes OriginReportId so its own report is excluded from the sweep.

Allow keeps its narrow sibling close: it performs no moderation action, and an
allow must not auto-dismiss a pending exam failure or content report.

HandleDenyAsync and WelcomeTimeoutJob drop their post-kick welcome deletes for
the same reason. The already-handled branch of WelcomeTimeoutJob keeps its
delete — that path returns without kicking.
EOF
```

---

### Task 6: Remove the welcome DM chat fallback

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs:1314-1340`

**Interfaces:**
- Consumes: nothing new.
- Produces: no signature change — `SendRulesAsync` keeps returning `DmDeliveryResult`.

**Context:** `SendRulesAsync` passes `fallbackChatId: chat.Id`, so a blocked DM makes `BotDmService.SendFallbackToChatAsync` post the full rules text into the group, mentioning the user. `HandleAcceptAsync` calls it at step 3, before any admission or ban check, so it fires regardless of the user's state. `BotDmService.SendFallbackToChatAsync` stays — `FileScanJob.cs:308` still uses it legitimately.

- [ ] **Step 1: Remove the fallback arguments**

Replace the body of `SendRulesAsync`'s delivery call:

```csharp
        // No chat fallback: a blocked DM must not spill the rules into the group as a
        // message addressed to a user who may already be banned or held for review.
        var result = await dmDeliveryService.SendDmAsync(
            user: UserIdentity.From(user),
            message: dmMessage,
            cancellationToken: cancellationToken);

        if (result.Failed)
        {
            logger.LogWarning(
                "Could not deliver welcome rules to {User} for {Chat}: {Error}",
                user.ToLogDebug(),
                chat.ToLogDebug(),
                result.ErrorMessage);
        }

        logger.LogDebug(
            "Rules sent to {User}: DmSent={DmSent}, FallbackUsed={FallbackUsed}",
            user.ToLogDebug(),
            result.DmSent,
            result.FallbackUsed);

        return result;
```

Update the method's doc comment to drop the "or fallback to chat" wording, and the same wording on `HandleAcceptAsync`'s step 3 comment:

```csharp
        // Step 3: Try to send rules via DM.
        // Always attempt this - previous DM sent via /start may have been deleted by user.
```

`dmResult.FallbackUsed` is now always `false` from this path; leave the `UpdateResponseAsync(..., dmResult.FallbackUsed, ...)` call as-is — it reports honestly, and the `dm_fallback` column stays meaningful for `FileScanJob`.

- [ ] **Step 2: Build and run the welcome tests**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~Welcome"`
Expected: PASS. If a test asserts the fallback chat id was passed, update it to assert `SendDmAsync` is called **without** a fallback chat.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs
git commit -F- <<'EOF'
fix(welcome): stop the DM fallback from posting rules into the group

SendRulesAsync passed fallbackChatId, so a user who never opened a DM with the
bot had the full rules posted into the group addressed to them — including
users already banned or held for admin review, since HandleAcceptAsync runs it
before any state check.

Undeliverable rules now log a warning instead. BotDmService's chat fallback
stays for FileScanJob, which uses it legitimately.
EOF
```

---

### Task 7: Full verification

- [ ] **Step 1: Apply migrations locally**

Run: `dotnet run --project TelegramGroupsAdmin --migrate-only`
Expected: the `AddContentUserIdToEnrichedReportsView` migration applies and the process exits cleanly.

- [ ] **Step 2: Build the solution**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: no errors, no new warnings.

- [ ] **Step 3: Run the unit suite**

Run: `dotnet test TelegramGroupsAdmin.UnitTests`
Expected: all PASS.

- [ ] **Step 4: Run the integration suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests`
Expected: all PASS, including `LoadCanonicalAsyncTests` (the canonical additions touch `reports`, whose count is not asserted).

- [ ] **Step 5: Push and open the PR**

```bash
git push -u origin fix/join-gate-cleanup
```

Open a PR to `develop`. If GitHub issues are filed for these three bugs first, put the `Closes #N` lines at the top of the PR body.
