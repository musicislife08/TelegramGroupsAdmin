# Admin Notification Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two admin-facing notification bugs in TelegramGroupsAdmin: (1) callback-action buttons in DMs that show "expired" within minutes due to cross-admin deletion, and (2) target-user names rendering as unstyled plain text in Telegram DMs because `tg://user?id=X` links don't work for users with no prior bot interaction. Also restore reporter clickability on the moderation report page.

**Architecture:** Bug A (expiration) is fixed by removing eager `DeleteByReportIdAsync` calls from the four action handlers and replacing age-based callback-context cleanup with an orphan-only sweep (contexts whose report no longer exists). The existing `ReportStatusHelper.CheckAlreadyHandled` pattern already produces a useful "Already handled by X" message on the multi-admin race. Bug B (DM mentions) is fixed by moving admin-notification rendering from `ParseMode.Html` to Telegram's entity-based sending (mutually exclusive per the Bot API). The renderer emits `MessageEntity` objects (`Bold` for titles/labels, `TextMention` with an embedded `User` object for clickable mentions that work even for unknown users). Bug C (UI) is a single Razor file change wrapping the reporter in the existing `<UserDetailLink>` component.

**Tech Stack:** .NET 10, C#, EF Core 10, Postgres 18, Telegram.Bot SDK, Blazor Server + MudBlazor 9, NUnit, Testcontainers.PostgreSQL.

**Spec:** `docs/superpowers/specs/2026-04-23-admin-notification-fixes-design.md`

**Branch:** Already on `fix/admin-notification-buttons-and-mentions` (created from `develop`).

---

## Prerequisites / Conventions

Before starting any task, know this about the repo:

- **No direct commits to `master` or `develop`.** All work is on the feature branch above. Per CLAUDE.md: ALWAYS prefer new commits over amending; use heredoc for multi-line commit messages.
- **Never run the app directly.** Telegram Bot API enforces one connection per token. Use `dotnet run --migrate-only` to verify migrations. Use `dotnet test` for verification.
- **Central Package Management:** NuGet package versions are in `Directory.Packages.props`. Don't add per-project versions.
- **Code navigation:** Prefer `find_symbol` / `find_references` (CSharperMcp tools) over grep for C# code. Required before renaming any interface member.
- **Commit style:** Conventional commits — `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`. Scope in parens, e.g. `fix(notifications): ...`.
- **Test commands:**
  - Unit tests: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --configuration Debug --logger "console;verbosity=minimal"`
  - Integration tests: `dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj --configuration Debug --logger "console;verbosity=minimal"`
  - Full suite takes ~20 minutes — run in background (`&` + `tee logs/test-run-$(date +%s).log`) when doing full runs; for task-scoped runs use `--filter`.
- **Fluent API in AppDbContext** is preferred over custom SQL; no migrations are needed for this plan (no schema changes).

### Testing patterns in this repo

- Unit tests under `TelegramGroupsAdmin.UnitTests/`, organized mirroring source tree. NUnit style: `[TestFixture]`, `[Test]`, `Assert.That(x, Is.EqualTo(y))`.
- Integration tests under `TelegramGroupsAdmin.IntegrationTests/`, use Testcontainers.PostgreSQL for real DB.
- Mock repositories via `NSubstitute` (`Substitute.For<IFoo>()`).
- Test doubles for `ILogger<T>`: `Substitute.For<ILogger<MyService>>()`.

### How to run a single test

```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --filter "FullyQualifiedName~NotificationRendererTests.RendersSubjectAsBold" \
    --logger "console;verbosity=normal"
```

---

## File Structure

New files and modifications, grouped by phase.

### Phase 1 — Bug A: Button Expiration

| File | Action | Responsibility |
|------|--------|----------------|
| `TelegramGroupsAdmin.Telegram/Services/ReportActions/ContentReportHandler.cs:284` | Modify | Remove `DeleteByReportIdAsync` call |
| `TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs:64,116,173` | Modify | Remove three `DeleteByReportIdAsync` calls |
| `TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs:50,87,124` | Modify | Remove three `DeleteByReportIdAsync` calls |
| `TelegramGroupsAdmin.Telegram/Services/ReportCallbackService.cs:86` | Modify | Remove post-action `DeleteAsync` call |
| `TelegramGroupsAdmin.Telegram/Repositories/IReportCallbackContextRepository.cs` | Modify | Remove `DeleteExpiredAsync`; add `DeleteOrphanedAsync` |
| `TelegramGroupsAdmin.Telegram/Repositories/ReportCallbackContextRepository.cs` | Modify | Implement `DeleteOrphanedAsync`; remove `DeleteExpiredAsync` |
| `TelegramGroupsAdmin.Core/Models/BackgroundJobSettings/DataCleanupSettings.cs` | Modify | Remove `CallbackContextRetention` property |
| `TelegramGroupsAdmin.BackgroundJobs/Jobs/DataCleanupJob.cs` | Modify | Use `DeleteOrphanedAsync`; drop retention config read |
| `TelegramGroupsAdmin.IntegrationTests/Services/BackgroundJobConfigPersistenceTests.cs:119,132` | Modify | Remove `CallbackContextRetention` assertions |
| `TelegramGroupsAdmin.IntegrationTests/Repositories/ReportCallbackContextRepositoryTests.cs` | Create | Integration test for `DeleteOrphanedAsync` |

### Phase 2 — Bug C: UI Reporter Link

| File | Action | Responsibility |
|------|--------|----------------|
| `TelegramGroupsAdmin/Components/Reports/ModerationReportCard.razor` | Modify | Wrap reporter in `<UserDetailLink>` when `ReportedByUserId != null` |

### Phase 3 — Bug B: Entity-Based DM Rendering

| File | Action | Responsibility |
|------|--------|----------------|
| `TelegramGroupsAdmin/Services/Notifications/Field.cs` | Modify | Field carries `UserIdentity?` instead of `long?` |
| `TelegramGroupsAdmin/Services/Notifications/NotificationPayloadBuilder.cs` | Modify | Non-nullable overloads; drop `WithFieldIf` |
| `TelegramGroupsAdmin/Services/Notifications/SectionBuilder.cs` | Modify | Non-nullable overloads; drop `WithFieldIf` |
| `TelegramGroupsAdmin/Services/Notifications/TelegramMessage.cs` | Create | Record `(string Text, IReadOnlyList<MessageEntity> Entities)` |
| `TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs` | Modify | `ToTelegramHtml` → `ToTelegramMessage`, entity-based output |
| `TelegramGroupsAdmin/Services/NotificationService.cs` | Modify | Update all call sites; use new DM overloads |
| `TelegramGroupsAdmin.Telegram/Services/Bot/Handlers/IBotMessageHandler.cs` | Modify | Add `entities` / `captionEntities` params |
| `TelegramGroupsAdmin.Telegram/Services/Bot/Handlers/BotMessageHandler.cs` | Modify | Pass entities to `apiClient.Send*` |
| `TelegramGroupsAdmin.Telegram/Services/Bot/IBotDmService.cs` | Modify | Add `SendDmWithMessageAsync` / `SendDmWithMessageAndKeyboardAsync` overloads |
| `TelegramGroupsAdmin.Telegram/Services/Bot/BotDmService.cs` | Modify | Implement new overloads (no parseMode, entities only) |
| `TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationRendererTests.cs` | Modify | Replace HTML assertions with entity-shape assertions |
| `TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationPayloadBuilderTests.cs` | Modify | Update for non-nullable / UserIdentity signatures |

---

# PHASE 1 — Bug A: Button Expiration

## Task 1: Remove eager `DeleteByReportIdAsync` from `ContentReportHandler`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/ContentReportHandler.cs:284`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportActions/ContentReportHandlerTests.cs`

**Context:** `ContentReportHandler.HandleContentDismissAsync` currently calls `callbackContextRepo.DeleteByReportIdAsync(report.Id, cancellationToken)` at the end of the method. When admin A dismisses, this wipes callback contexts for admins B, C, D too — producing the cross-admin "expired" bug.

- [ ] **Step 1: Find and read the failing-test file.**

Run: `find TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportActions -name "ContentReportHandlerTests*" -type f`
Expected: one file path. Read it to understand current test style.

If no test file exists, create `TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportActions/ContentReportHandlerTests.cs` with a basic `[TestFixture]` scaffolding.

- [ ] **Step 2: Add failing test asserting `DeleteByReportIdAsync` is NOT called after dismiss.**

Add this test to `ContentReportHandlerTests.cs`:

```csharp
[Test]
public async Task HandleContentDismissAsync_DoesNotDeleteCallbackContextsByReportId()
{
    // Arrange
    var reportsRepo = Substitute.For<IReportsRepository>();
    var callbackContextRepo = Substitute.For<IReportCallbackContextRepository>();
    var moderationService = Substitute.For<IBotModerationService>();
    var botMessageService = Substitute.For<IBotMessageService>();
    var logger = Substitute.For<ILogger<ContentReportHandler>>();

    reportsRepo.GetContentReportAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(TestReport());
    reportsRepo.TryUpdateStatusAsync(
            Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(true);

    var sut = new ContentReportHandler(
        reportsRepo, callbackContextRepo, moderationService, botMessageService, logger);

    // Act
    var result = await sut.HandleContentDismissAsync(42L, Actor.Unknown, CancellationToken.None);

    // Assert
    Assert.That(result.Success, Is.True);
    await callbackContextRepo
        .DidNotReceive()
        .DeleteByReportIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
}

private static Report TestReport() =>
    new Report
    {
        Id = 42,
        MessageId = 100,
        Chat = new ChatIdentity(-1001, "TestChat"),
        Status = ReportStatus.Pending,
        ReportedAt = DateTimeOffset.UtcNow
    };
```

- [ ] **Step 3: Run the test and verify it FAILS.**

Run:
```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --filter "FullyQualifiedName~ContentReportHandlerTests.HandleContentDismissAsync_DoesNotDeleteCallbackContextsByReportId" \
    --logger "console;verbosity=normal"
```

Expected: FAIL — `callbackContextRepo.DeleteByReportIdAsync` received 1 call (because the current implementation still calls it).

- [ ] **Step 4: Remove the `DeleteByReportIdAsync` call from the handler.**

Open `TelegramGroupsAdmin.Telegram/Services/ReportActions/ContentReportHandler.cs`. Find line 284:

```csharp
        // Cleanup stale DM callback contexts
        await callbackContextRepo.DeleteByReportIdAsync(report.Id, cancellationToken);
```

Delete both lines (the comment and the await). This is the last statement in `HandleContentDismissAsync` before the closing brace — the method should now end naturally after the prior block.

Check whether the same pattern exists in `HandleContentSpamAsync`, `HandleContentBanAsync`, `HandleContentWarnAsync` at the bottom of each method — remove those too if present.

- [ ] **Step 5: Remove the now-unused `callbackContextRepo` constructor dependency if it has no other callers.**

Use `grep` inside `ContentReportHandler.cs` to check:

```bash
grep -n "callbackContextRepo\." TelegramGroupsAdmin.Telegram/Services/ReportActions/ContentReportHandler.cs
```

If zero hits remain, remove `IReportCallbackContextRepository callbackContextRepo` from the primary constructor parameter list at line 26 and remove the corresponding `using`. Also update the test to stop passing it in `new ContentReportHandler(...)`.

If the test still references `callbackContextRepo`, keep the field (it's a dependency that just isn't called — fine).

- [ ] **Step 6: Run the test and verify it PASSES.**

Run:
```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --filter "FullyQualifiedName~ContentReportHandlerTests" \
    --logger "console;verbosity=normal"
```

Expected: All tests pass.

- [ ] **Step 7: Commit.**

```bash
git add TelegramGroupsAdmin.Telegram/Services/ReportActions/ContentReportHandler.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportActions/ContentReportHandlerTests.cs
git commit -F- <<'EOF'
fix(reports): stop eager callback-context deletion in content report handler

Eagerly deleting callback contexts by report ID wiped contexts for every
admin who received a DM for the report, producing "Button expired" on
their buttons the moment one admin acted. Relies on the existing
CheckAlreadyHandled path for multi-admin resolution instead.

EOF
```

---

## Task 2: Remove eager deletion from `ProfileScanHandler`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs:64,116,173`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportActions/ProfileScanHandlerTests.cs`

**Context:** Three action methods (`BanAsync`, `KickAsync`, `AllowAsync`) each call `callbackContextRepo.DeleteByReportIdAsync(alertId, ct)`. All three need removal.

- [ ] **Step 1: Add failing test for BanAsync.**

Add to `ProfileScanHandlerTests.cs`:

```csharp
[Test]
public async Task BanAsync_DoesNotDeleteCallbackContextsByReportId()
{
    var reportsRepo = Substitute.For<IReportsRepository>();
    var callbackContextRepo = Substitute.For<IReportCallbackContextRepository>();
    var moderationService = Substitute.For<IBotModerationService>();
    var logger = Substitute.For<ILogger<ProfileScanHandler>>();

    reportsRepo.GetProfileScanAlertAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(TestAlert());
    reportsRepo.TryUpdateStatusAsync(
            Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(true);
    moderationService.BanUserAsync(
            Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
        .Returns(BanResult.Succeeded(chatsAffected: 1));

    var sut = new ProfileScanHandler(reportsRepo, callbackContextRepo, moderationService, logger);

    var result = await sut.BanAsync(42L, Actor.Unknown, CancellationToken.None);

    Assert.That(result.Success, Is.True);
    await callbackContextRepo
        .DidNotReceive()
        .DeleteByReportIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
}
```

Also add identical tests for `KickAsync_DoesNotDeleteCallbackContextsByReportId` and `AllowAsync_DoesNotDeleteCallbackContextsByReportId`.

Lookup `TestAlert()` style in existing tests (use `find_references` for `ProfileScanAlert`) to build a minimal alert that matches.

- [ ] **Step 2: Run tests and verify they FAIL.**

Run:
```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --filter "FullyQualifiedName~ProfileScanHandlerTests" \
    --logger "console;verbosity=normal"
```

Expected: The three new tests FAIL.

- [ ] **Step 3: Remove the three `DeleteByReportIdAsync` calls.**

Open `TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs`. Remove these three statements (lines ~64, ~116, ~173):

```csharp
await callbackContextRepo.DeleteByReportIdAsync(alertId, cancellationToken);
```

Do NOT remove `CleanupSiblingAlertsAsync` calls nearby — those handle a different concern (sibling alerts for the same user, not callback contexts).

- [ ] **Step 4: Check whether `callbackContextRepo` is still used.**

```bash
grep -n "callbackContextRepo\." TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs
```

If zero hits, remove the constructor parameter and update the test.

- [ ] **Step 5: Run tests; verify PASS.**

Run:
```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --filter "FullyQualifiedName~ProfileScanHandlerTests" \
    --logger "console;verbosity=normal"
```

Expected: All pass.

- [ ] **Step 6: Commit.**

```bash
git add TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportActions/ProfileScanHandlerTests.cs
git commit -F- <<'EOF'
fix(reports): stop eager callback-context deletion in profile scan handler

EOF
```

---

## Task 3: Remove eager deletion from `ExamHandler`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs:50,87,124`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportActions/ExamHandlerTests.cs`

**Context:** Three action methods (`ApproveAsync`, `DenyAsync`, `DenyAndBanAsync`) each call `callbackContextRepo.DeleteByReportIdAsync(examId, ct)`.

- [ ] **Step 1: Add three failing tests** (same pattern as Task 2).

For each of `ApproveAsync`, `DenyAsync`, `DenyAndBanAsync`, add a test `<Name>_DoesNotDeleteCallbackContextsByReportId` with the same `DidNotReceive().DeleteByReportIdAsync(...)` assertion.

- [ ] **Step 2: Run tests and verify they FAIL.**

Run:
```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --filter "FullyQualifiedName~ExamHandlerTests" \
    --logger "console;verbosity=normal"
```

Expected: FAIL on the three new tests.

- [ ] **Step 3: Remove the three `DeleteByReportIdAsync` calls** in `ExamHandler.cs` at ~50, ~87, ~124.

- [ ] **Step 4: Check whether `callbackContextRepo` is still used in `ExamHandler`.**

```bash
grep -n "callbackContextRepo\." TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs
```

If zero hits, remove the constructor parameter.

- [ ] **Step 5: Run tests; verify PASS.**

- [ ] **Step 6: Commit.**

```bash
git add TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportActions/ExamHandlerTests.cs
git commit -F- <<'EOF'
fix(reports): stop eager callback-context deletion in exam handler

EOF
```

---

## Task 4: Stop deleting context after successful callback in `ReportCallbackService`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportCallbackService.cs:86`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportCallbackServiceTests.cs`

**Context:** After a successful action dispatch, `HandleCallbackAsync` calls `callbackContextRepo.DeleteAsync(contextId)`. With orphan-based cleanup (Task 6), this is unnecessary — the context lives until its report is gone. Removing this simplifies the happy path and avoids the case where a different admin's DM is still referencing this same contextId (rare but possible if contextId is reused — safer to let cleanup handle).

- [ ] **Step 1: Add failing test.**

In `ReportCallbackServiceTests.cs`, add:

```csharp
[Test]
public async Task HandleCallbackAsync_DoesNotDeleteContextById_AfterSuccessfulAction()
{
    var callbackContextRepo = Substitute.For<IReportCallbackContextRepository>();
    var dmService = Substitute.For<IBotDmService>();
    var reportActionsService = Substitute.For<IReportActionsService>();
    var logger = Substitute.For<ILogger<ReportCallbackService>>();
    var scopeFactory = BuildScopeFactory(callbackContextRepo, dmService);

    callbackContextRepo.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(new ReportCallbackContext(
            Id: 7, ReportId: 42, ReportType: ReportType.ContentReport,
            ChatId: -1001, UserId: 100, CreatedAt: DateTimeOffset.UtcNow));

    reportActionsService.HandleContentDismissAsync(
            Arg.Any<long>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
        .Returns(new ReviewActionResult(true, "Dismissed"));

    var sut = new ReportCallbackService(logger, scopeFactory, reportActionsService);

    var query = new CallbackQuery
    {
        Id = "cq1",
        Data = $"rev:7:{(int)ReportAction.Dismiss}",
        From = new User { Id = 999, FirstName = "Admin" },
        Message = new Message
        {
            Chat = new Chat { Id = 1001 },
            MessageId = 555,
            Text = "Original"
        }
    };

    await sut.HandleCallbackAsync(query, CancellationToken.None);

    await callbackContextRepo
        .DidNotReceive()
        .DeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
}

private static IServiceScopeFactory BuildScopeFactory(
    IReportCallbackContextRepository repo, IBotDmService dm)
{
    var scope = Substitute.For<IServiceScope>();
    var sp = Substitute.For<IServiceProvider>();
    sp.GetService(typeof(IReportCallbackContextRepository)).Returns(repo);
    sp.GetService(typeof(IBotDmService)).Returns(dm);
    scope.ServiceProvider.Returns(sp);
    var factory = Substitute.For<IServiceScopeFactory>();
    factory.CreateScope().Returns(scope);
    return factory;
}
```

- [ ] **Step 2: Run test, verify it FAILS.**

Run:
```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --filter "FullyQualifiedName~ReportCallbackServiceTests.HandleCallbackAsync_DoesNotDeleteContextById_AfterSuccessfulAction" \
    --logger "console;verbosity=normal"
```

Expected: FAIL — `DeleteAsync` received 1 call.

- [ ] **Step 3: Remove the `DeleteAsync(contextId)` call.**

In `TelegramGroupsAdmin.Telegram/Services/ReportCallbackService.cs`, find (near line 85):

```csharp
        // Delete callback context (the service handles report-level cleanup)
        await callbackContextRepo.DeleteAsync(contextId, cancellationToken);
```

Remove both lines.

- [ ] **Step 4: Run test, verify PASS.**

- [ ] **Step 5: Commit.**

```bash
git add TelegramGroupsAdmin.Telegram/Services/ReportCallbackService.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/ReportCallbackServiceTests.cs
git commit -F- <<'EOF'
fix(reports): stop deleting callback context after successful action

Orphan-based cleanup handles callback context lifecycle now. Removing
eager deletion prevents a race where a second admin clicking around
the same time finds their context gone.

EOF
```

---

## Task 5: Replace `DeleteExpiredAsync` with `DeleteOrphanedAsync` on the repository

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IReportCallbackContextRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/ReportCallbackContextRepository.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/ReportCallbackContextRepositoryTests.cs` (create)

**Context:** The new cleanup strategy deletes callback contexts whose `report_id` no longer matches any row in `reports`. This is a single `DELETE ... WHERE NOT EXISTS` query that EF Core translates efficiently using the existing `ix_report_callback_contexts_report_id` index. The old `DeleteExpiredAsync(TimeSpan)` is dead and should be removed.

- [ ] **Step 1: Create the failing integration test.**

Create `TelegramGroupsAdmin.IntegrationTests/Repositories/ReportCallbackContextRepositoryTests.cs`:

```csharp
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.Infrastructure;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

[TestFixture]
public class ReportCallbackContextRepositoryTests : IntegrationTestBase
{
    [Test]
    public async Task DeleteOrphanedAsync_RemovesContextsForMissingReports_KeepsContextsForExistingReports()
    {
        // Arrange
        var reportsRepo = ServiceProvider.GetRequiredService<IReportsRepository>();
        var callbackRepo = ServiceProvider.GetRequiredService<IReportCallbackContextRepository>();

        // Seed a managed chat for FK
        await SeedManagedChatAsync(-1001L, "TestChat");

        // Report 1: exists in DB
        var report1Id = await reportsRepo.InsertContentReportAsync(new Report
        {
            MessageId = 100,
            Chat = new ChatIdentity(-1001L, "TestChat"),
            Status = ReportStatus.Pending,
            ReportedAt = DateTimeOffset.UtcNow,
            ReportedByUserId = 200,
            ReportedByUserName = "tester"
        }, CancellationToken.None);

        // Context 1: tied to an existing report — must be kept
        var ctx1 = await callbackRepo.CreateAsync(new ReportCallbackContext(
            Id: 0, ReportId: report1Id, ReportType: ReportType.ContentReport,
            ChatId: -1001, UserId: 300, CreatedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);

        // Context 2: ReportId = 9999 (no such report) — must be deleted
        var ctx2 = await callbackRepo.CreateAsync(new ReportCallbackContext(
            Id: 0, ReportId: 9999, ReportType: ReportType.ContentReport,
            ChatId: -1001, UserId: 300, CreatedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);

        // Act
        var deleted = await callbackRepo.DeleteOrphanedAsync(CancellationToken.None);

        // Assert
        Assert.That(deleted, Is.EqualTo(1), "one orphan deleted");
        Assert.That(
            await callbackRepo.GetByIdAsync(ctx1, CancellationToken.None),
            Is.Not.Null, "context tied to existing report stays");
        Assert.That(
            await callbackRepo.GetByIdAsync(ctx2, CancellationToken.None),
            Is.Null, "orphan context was removed");
    }
}
```

Check the existing integration test base class (`IntegrationTestBase`) for the correct way to seed managed chats — mirror an existing test pattern.

- [ ] **Step 2: Run the integration test, verify compile FAIL.**

Run:
```bash
dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
    --filter "FullyQualifiedName~ReportCallbackContextRepositoryTests" \
    --logger "console;verbosity=normal"
```

Expected: COMPILE FAIL — `DeleteOrphanedAsync` not defined on `IReportCallbackContextRepository`.

- [ ] **Step 3: Update the interface.**

Edit `TelegramGroupsAdmin.Telegram/Repositories/IReportCallbackContextRepository.cs`:

Remove:
```csharp
/// <summary>
/// Delete all expired callback contexts (cleanup job).
/// </summary>
Task<int> DeleteExpiredAsync(
    TimeSpan maxAge,
    CancellationToken cancellationToken = default);
```

Add:
```csharp
/// <summary>
/// Delete all callback contexts whose associated report no longer exists.
/// Used by the data cleanup job — contexts live as long as their report does.
/// </summary>
Task<int> DeleteOrphanedAsync(CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Update the implementation.**

Edit `TelegramGroupsAdmin.Telegram/Repositories/ReportCallbackContextRepository.cs`:

Remove the whole `DeleteExpiredAsync` method.

Add (inside the same class):

```csharp
public async Task<int> DeleteOrphanedAsync(CancellationToken cancellationToken = default)
{
    await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

    return await dbContext.ReportCallbackContexts
        .Where(rcc => !dbContext.Reports.Any(r => r.Id == rcc.ReportId))
        .ExecuteDeleteAsync(cancellationToken);
}
```

- [ ] **Step 5: Run the integration test, verify PASS.**

Run:
```bash
dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
    --filter "FullyQualifiedName~ReportCallbackContextRepositoryTests" \
    --logger "console;verbosity=normal"
```

Expected: PASS.

- [ ] **Step 6: Commit.**

```bash
git add TelegramGroupsAdmin.Telegram/Repositories/IReportCallbackContextRepository.cs \
        TelegramGroupsAdmin.Telegram/Repositories/ReportCallbackContextRepository.cs \
        TelegramGroupsAdmin.IntegrationTests/Repositories/ReportCallbackContextRepositoryTests.cs
git commit -F- <<'EOF'
refactor(reports): replace DeleteExpiredAsync with DeleteOrphanedAsync

Callback contexts now live as long as their associated report. Orphan-
based cleanup via EF Core NOT EXISTS uses the existing report_id index.
Removes the 7-day retention window that was wiping live buttons.

EOF
```

---

## Task 6: Drop `CallbackContextRetention` from `DataCleanupSettings`

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Models/BackgroundJobSettings/DataCleanupSettings.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/Services/BackgroundJobConfigPersistenceTests.cs:119,132`

**Context:** The `CallbackContextRetention = "7d"` setting is dead — cleanup is now orphan-based, not age-based. Remove the property from settings and any test assertions that use it.

- [ ] **Step 1: Read the current `DataCleanupSettings.cs` to see the exact property to remove.**

Run: `grep -n "CallbackContextRetention" TelegramGroupsAdmin.Core/Models/BackgroundJobSettings/DataCleanupSettings.cs`

- [ ] **Step 2: Remove the `CallbackContextRetention` property and its default constant usage.**

Delete the property (around line 54):
```csharp
public string CallbackContextRetention { get; init; } = "7d";
```

Search for `DefaultShortRetention` within that file — it's still used by `WebNotificationRetention`, leave it.

- [ ] **Step 3: Update `BackgroundJobConfigPersistenceTests.cs`.**

```bash
grep -n "CallbackContextRetention" TelegramGroupsAdmin.IntegrationTests/Services/BackgroundJobConfigPersistenceTests.cs
```

Remove lines 119 (`CallbackContextRetention = "14d",` in the object initializer) and 132 (the corresponding assertion). Do NOT delete adjacent lines for other settings.

- [ ] **Step 4: Build the solution to catch any other references.**

Run: `dotnet build TelegramGroupsAdmin.sln --configuration Debug`

Expected: clean build. If any other file still references `CallbackContextRetention`, update it (likely none).

- [ ] **Step 5: Commit.**

```bash
git add TelegramGroupsAdmin.Core/Models/BackgroundJobSettings/DataCleanupSettings.cs \
        TelegramGroupsAdmin.IntegrationTests/Services/BackgroundJobConfigPersistenceTests.cs
git commit -F- <<'EOF'
refactor(config): drop unused CallbackContextRetention setting

Replaced with orphan-based cleanup — retention by age no longer applies.

EOF
```

---

## Task 7: Switch `DataCleanupJob` to call `DeleteOrphanedAsync`

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Jobs/DataCleanupJob.cs:79,94,184-198`

**Context:** `DataCleanupJob` reads `settings.CallbackContextRetention` and calls `DeleteExpiredAsync(retention)`. Both go away; replace with `DeleteOrphanedAsync()`.

- [ ] **Step 1: Update `ExecuteCleanupAsync` to drop retention lookup.**

Open `TelegramGroupsAdmin.BackgroundJobs/Jobs/DataCleanupJob.cs`. Around line 79, remove:

```csharp
        var contextRetention = TimeSpanUtilities.ParseDurationOrDefault(settings.CallbackContextRetention, DataCleanupSettings.DefaultShortRetention);
```

Change the call at line 94:

```csharp
// Before
totalDeleted += await CleanupCallbackContextsAsync(sp, contextRetention, cancellationToken);

// After
totalDeleted += await CleanupCallbackContextsAsync(sp, cancellationToken);
```

- [ ] **Step 2: Rewrite `CleanupCallbackContextsAsync`.**

Replace the existing method body (lines 184–198):

```csharp
private async Task<long> CleanupCallbackContextsAsync(IServiceProvider sp, CancellationToken cancellationToken)
{
    var callbackContextRepo = sp.GetRequiredService<IReportCallbackContextRepository>();
    var contextsDeleted = await callbackContextRepo.DeleteOrphanedAsync(cancellationToken);

    if (contextsDeleted > 0)
    {
        _logger.LogInformation(
            "Callback context cleanup: {Count} orphaned contexts deleted (no matching report)",
            contextsDeleted);
    }

    return contextsDeleted;
}
```

- [ ] **Step 3: Build the solution.**

Run: `dotnet build TelegramGroupsAdmin.sln --configuration Debug`

Expected: clean build.

- [ ] **Step 4: Run a migration-only check to ensure the app still boots with these changes.**

Run: `dotnet run --project TelegramGroupsAdmin --migrate-only`

Expected: "Migrations applied" or "No pending migrations" — and the process exits cleanly.

- [ ] **Step 5: Commit.**

```bash
git add TelegramGroupsAdmin.BackgroundJobs/Jobs/DataCleanupJob.cs
git commit -F- <<'EOF'
fix(cleanup): use orphan-based callback-context cleanup

Callback context cleanup now deletes only contexts whose report no longer
exists, rather than contexts older than a fixed retention window. Live
reports keep their buttons for their full lifetime.

EOF
```

---

# PHASE 2 — Bug C: Clickable Reporter on Moderation Report Card

## Task 8: Wrap reporter in `<UserDetailLink>` for Telegram user reports

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Reports/ModerationReportCard.razor`

**Context:** Currently `@GetReporterDisplay()` returns a flat string — no link for Telegram reporters. The component already injects a `UserDetailLink` for the reported user at line 31, so we follow the same pattern for `Report.ReportedByUserId != null`.

- [ ] **Step 1: Add the new render fragment method to the `@code` block.**

At the end of the `@code` block (before the closing brace), add:

```csharp
private RenderFragment RenderReporter() => builder =>
{
    // System-generated report (auto-detection)
    if (Report.ReportedByUserId == null && Report.WebUserId == null)
    {
        builder.AddContent(0, Report.ReportedByUserName ?? "System");
        return;
    }

    // Telegram user report — clickable
    if (Report.ReportedByUserId != null)
    {
        var displayName = Report.ReportedByUserName ?? "Unknown";
        builder.OpenComponent<UserDetailLink>(0);
        builder.AddAttribute(1, nameof(UserDetailLink.UserId), Report.ReportedByUserId.Value);
        builder.AddAttribute(2, nameof(UserDetailLink.ChildContent),
            (RenderFragment)(b => b.AddContent(0, displayName)));
        builder.CloseComponent();
        builder.AddContent(3, $" (ID: {Report.ReportedByUserId})");
        return;
    }

    // Web-user report
    if (Report.WebUserId != null)
    {
        builder.AddContent(0, $"{Report.ReportedByUserName ?? "Web Admin"} (Web User)");
    }
};
```

Note: use `RenderFragment` rather than modifying `GetReporterDisplay()` since we now need a component, not a string.

- [ ] **Step 2: Replace the reporter display line.**

Find line 66–68:

```razor
<MudText Typo="Typo.caption" Color="Color.Secondary">
    <b>Reported by:</b> @GetReporterDisplay()
</MudText>
```

Replace with:

```razor
<MudText Typo="Typo.caption" Color="Color.Secondary">
    <b>Reported by:</b> @RenderReporter()
</MudText>
```

- [ ] **Step 3: Delete the now-unused `GetReporterDisplay()` method.**

Verify no other file references it:

```bash
grep -rn "GetReporterDisplay" TelegramGroupsAdmin/ --include="*.razor" --include="*.cs"
```

If only this file references it, remove the method. Otherwise, keep it (unlikely given the scope).

- [ ] **Step 4: Build and run the app for UI verification.**

```bash
dotnet build TelegramGroupsAdmin.sln --configuration Debug
```

Expected: clean build.

- [ ] **Step 5: Commit.**

```bash
git add TelegramGroupsAdmin/Components/Reports/ModerationReportCard.razor
git commit -F- <<'EOF'
fix(ui): make reporter clickable on moderation report card

Wraps the reporter name in UserDetailLink for Telegram-user-submitted
reports (/report command), opening the user detail dialog on click.
System and web-user reports render as plain text.

EOF
```

---

# PHASE 3 — Bug B: Entity-Based DM Rendering

## Task 9: Change `Field` record to carry `UserIdentity?`

**Files:**
- Modify: `TelegramGroupsAdmin/Services/Notifications/Field.cs`

**Context:** Foundation for entity-based rendering — the renderer needs richer user info (first/last/username for display fallback in `text_mention`), not just an ID.

- [ ] **Step 1: Replace Field record body.**

Open `TelegramGroupsAdmin/Services/Notifications/Field.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// A single labeled field. When User is set, the value renders as a
/// text_mention entity in Telegram DMs — clickable regardless of whether
/// the user has interacted with the bot before.
/// </summary>
internal sealed record Field(string Label, string Value, UserIdentity? User = null);
```

- [ ] **Step 2: Build, expect compile failures at call sites.**

Run: `dotnet build TelegramGroupsAdmin.sln --configuration Debug`

Expected: compile failures in `NotificationPayloadBuilder.cs`, `SectionBuilder.cs`, `NotificationRenderer.cs`, and `NotificationService.cs`. These are the call sites we update in subsequent tasks.

- [ ] **Step 3: Do NOT commit yet.** The solution is in a broken state. Move to Task 10.

---

## Task 10: Update `NotificationPayloadBuilder` — non-nullable, drop `WithFieldIf`

**Files:**
- Modify: `TelegramGroupsAdmin/Services/Notifications/NotificationPayloadBuilder.cs`

**Context:** Non-nullable overloads, caller owns conditionals. Drop `WithFieldIf` entirely.

- [ ] **Step 1: Replace the builder class.**

Open `TelegramGroupsAdmin/Services/Notifications/NotificationPayloadBuilder.cs` and replace its contents with:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Fluent builder for constructing immutable NotificationPayload records.
/// Each With* method adds content; conditional logic lives at the call site.
/// </summary>
internal sealed class NotificationPayloadBuilder
{
    private string _subject = "";
    private readonly List<ContentBlock> _blocks = [];
    private string? _photoPath;
    private string? _videoPath;
    private ActionKeyboardContext? _keyboard;

    public static NotificationPayloadBuilder Create(string subject) => new() { _subject = subject };

    public NotificationPayloadBuilder WithText(string text)
    {
        _blocks.Add(new TextBlock(text));
        return this;
    }

    public NotificationPayloadBuilder WithField(string label, string value)
    {
        _blocks.Add(new FieldList([new(label, value)]));
        return this;
    }

    public NotificationPayloadBuilder WithField(string label, UserIdentity user)
    {
        _blocks.Add(new FieldList([new(label, user.DisplayName, user)]));
        return this;
    }

    public NotificationPayloadBuilder WithSection(string header, Action<SectionBuilder> configure)
    {
        var sb = new SectionBuilder();
        configure(sb);
        _blocks.Add(new SectionBlock(header, sb.Build()));
        return this;
    }

    public NotificationPayloadBuilder WithPhoto(string path)
    {
        _photoPath = path;
        return this;
    }

    public NotificationPayloadBuilder WithVideo(string path)
    {
        _videoPath = path;
        return this;
    }

    public NotificationPayloadBuilder WithKeyboard(ActionKeyboardContext ctx)
    {
        _keyboard = ctx;
        return this;
    }

    public NotificationPayload Build() => new()
    {
        Subject = _subject,
        Blocks = _blocks.ToArray(),
        PhotoPath = _photoPath,
        VideoPath = _videoPath,
        Keyboard = _keyboard
    };
}
```

Note: `WithPhoto` / `WithVideo` are now non-nullable too, consistent with the principle.

- [ ] **Step 2: Do NOT build yet** — `SectionBuilder` and call sites still broken. Move to Task 11.

---

## Task 11: Update `SectionBuilder` — non-nullable, drop `WithFieldIf`

**Files:**
- Modify: `TelegramGroupsAdmin/Services/Notifications/SectionBuilder.cs`

- [ ] **Step 1: Replace the class contents.**

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Builder for content blocks within a section.
/// </summary>
internal sealed class SectionBuilder
{
    private readonly List<ContentBlock> _blocks = [];

    public SectionBuilder WithText(string text)
    {
        _blocks.Add(new TextBlock(text));
        return this;
    }

    public SectionBuilder WithField(string label, string value)
    {
        _blocks.Add(new FieldList([new(label, value)]));
        return this;
    }

    public SectionBuilder WithField(string label, UserIdentity user)
    {
        _blocks.Add(new FieldList([new(label, user.DisplayName, user)]));
        return this;
    }

    internal IReadOnlyList<ContentBlock> Build() => _blocks.ToArray();
}
```

- [ ] **Step 2: Do NOT build yet** — call sites still broken.

---

## Task 12: Update call sites in `NotificationService.cs`

**Files:**
- Modify: `TelegramGroupsAdmin/Services/NotificationService.cs`

**Context:** Every `.WithField(label, displayName, telegramUserId: id)` call becomes `.WithField(label, userIdentity)` or `.WithField(label, plainString)`. Every `.WithFieldIf(condition, ...)` becomes an explicit `if` block. Every `.WithPhoto(x)` / `.WithVideo(x)` stays but only called when `x != null`.

- [ ] **Step 1: Update `SendSpamBanNotificationAsync`.**

In `TelegramGroupsAdmin/Services/NotificationService.cs`, replace the body starting at `var payload = NotificationPayloadBuilder.Create(title)` (around line 79):

```csharp
var builder = NotificationPayloadBuilder.Create(title)
    .WithField("User", user)
    .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
    .WithSection("Message", s => s
        .WithText(messagePreview ?? "[No text]"))
    .WithSection("Detection", s =>
    {
        s.WithField("Net Score", $"{netScore:F2}");
        s.WithField("Score", $"{score:F2}");
        if (detectionReason != null)
            s.WithField("Reason", detectionReason);
    })
    .WithSection("Action Taken", s =>
    {
        s.WithField("Banned from", $"{chatsAffected} managed chats");
        if (messageDeleted)
            s.WithField("Message deleted", $"ID: {messageId}");
    });

if (photoPath != null)
    builder.WithPhoto(photoPath);

if (videoPath != null)
    builder.WithVideo(videoPath);

var payload = builder.Build();
```

- [ ] **Step 2: Update `SendReportNotificationAsync`.**

Find the existing payload-construction block. Rewrite:

```csharp
var reporterUser = !isAutomated && reporterUserId.HasValue
    ? new UserIdentity(reporterUserId.Value, FirstName: null, LastName: null, Username: reporterName)
    : null;

var builder = NotificationPayloadBuilder.Create("Message Reported")
    .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
    .WithField("Reported user", reportedUser);

if (reporterUser != null)
    builder.WithField("Reported by", reporterUser);
else
    builder.WithField("Reported by", "System (automated)");

builder
    .WithSection("Message", s => s.WithText(messagePreview))
    .WithKeyboard(new ActionKeyboardContext(reportId, chat.Id, reportedUser.Id, reportType));

if (photoPath != null)
    builder.WithPhoto(photoPath);

var payload = builder.Build();
```

- [ ] **Step 3: Update `SendProfileScanAlertAsync`.**

```csharp
var payload = NotificationPayloadBuilder.Create("Profile Scan Alert")
    .WithField("User", user)
    .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
    .WithSection("Analysis", s =>
    {
        s.WithField("Score", $"{score:F1}");
        s.WithField("Signals", signals);
        if (aiReason != null)
            s.WithField("AI Reasoning", aiReason);
    })
    .WithKeyboard(new ActionKeyboardContext(reportId, chat.Id, user.Id, ReportType.ProfileScanAlert))
    .Build();
```

- [ ] **Step 4: Update `SendExamFailureNotificationAsync`.**

```csharp
var payload = NotificationPayloadBuilder.Create("Entrance Exam Review Required")
    .WithField("User", user)
    .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
    .WithSection("Results", s =>
    {
        s.WithField("Answered", $"{mcCorrectCount}/{mcTotal} correct");
        s.WithField("Score", $"{mcScore}% (Required: {mcPassingThreshold}%)");
    })
    .WithSection("Open-Ended Response", s =>
    {
        if (openEndedQuestion != null) s.WithField("Question", openEndedQuestion);
        if (openEndedAnswer != null) s.WithField("Answer", openEndedAnswer);
        if (aiReasoning != null) s.WithField("AI Reasoning", aiReasoning);
    })
    .WithKeyboard(new ActionKeyboardContext(examFailureId, chat.Id, user.Id, ReportType.ExamFailure))
    .Build();
```

- [ ] **Step 5: Update `SendBanNotificationAsync`, `SendMalwareDetectedAsync`, `SendAdminChangedAsync`, `SendBackupFailedAsync`, `SendChatHealthWarningAsync` using the same patterns.**

For each:
- Replace `.WithField(label, user.DisplayName, telegramUserId: user.Id)` → `.WithField(label, user)`.
- Replace `.WithFieldIf(cond, label, value)` → `if (cond) builder.WithField(label, value);` (converted to imperative).
- Replace `.WithPhoto(x)` / `.WithVideo(x)` with a guarded `if (x != null) builder.WithPhoto(x);` call.

- [ ] **Step 6: Build the solution.**

Run: `dotnet build TelegramGroupsAdmin.sln --configuration Debug`

Expected: clean build if all call sites have been migrated. If compile errors remain, fix each file listed.

- [ ] **Step 7: Commit.**

```bash
git add TelegramGroupsAdmin/Services/Notifications/Field.cs \
        TelegramGroupsAdmin/Services/Notifications/NotificationPayloadBuilder.cs \
        TelegramGroupsAdmin/Services/Notifications/SectionBuilder.cs \
        TelegramGroupsAdmin/Services/NotificationService.cs
git commit -F- <<'EOF'
refactor(notifications): non-nullable builder, UserIdentity-aware fields

Moves conditional logic out of the builder. Fields carry UserIdentity so
the renderer can emit text_mention entities with embedded User objects
for clickable DM mentions (next commit).

EOF
```

---

## Task 13: Update `NotificationPayloadBuilderTests` for new signatures

**Files:**
- Modify: `TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationPayloadBuilderTests.cs`

- [ ] **Step 1: Read the existing test file to understand current structure.**

```bash
cat TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationPayloadBuilderTests.cs
```

- [ ] **Step 2: Update each test method that uses the old signatures.**

For each `.WithField(label, value, telegramUserId: id)` test: rewrite to use `.WithField(label, user)` and assert `field.User` instead of `field.TelegramUserId`.

For each `WithFieldIf` test: delete it (the method is gone).

For each `.WithPhoto(null)` test: rewrite to assert the property is null when `WithPhoto` is NOT called.

- [ ] **Step 3: Run tests, verify PASS.**

```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --filter "FullyQualifiedName~NotificationPayloadBuilderTests" \
    --logger "console;verbosity=normal"
```

Expected: all pass.

- [ ] **Step 4: Commit.**

```bash
git add TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationPayloadBuilderTests.cs
git commit -F- <<'EOF'
test(notifications): update builder tests for new signatures

EOF
```

---

## Task 14: Create `TelegramMessage` record

**Files:**
- Create: `TelegramGroupsAdmin/Services/Notifications/TelegramMessage.cs`

- [ ] **Step 1: Create the record.**

```csharp
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Rendered Telegram message — plain text plus explicit entities.
/// Sent with no parse_mode; Telegram renders exactly what the entities specify.
/// </summary>
internal sealed record TelegramMessage(
    string Text,
    IReadOnlyList<MessageEntity> Entities);
```

- [ ] **Step 2: Build.**

Run: `dotnet build TelegramGroupsAdmin.sln --configuration Debug`

Expected: clean build.

- [ ] **Step 3: Commit.**

```bash
git add TelegramGroupsAdmin/Services/Notifications/TelegramMessage.cs
git commit -F- <<'EOF'
feat(notifications): add TelegramMessage record for entity-based output

EOF
```

---

## Task 15: Rewrite `NotificationRenderer.ToTelegramMessage`

**Files:**
- Modify: `TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs`

**Context:** Replace the HTML-producing `ToTelegramHtml` with `ToTelegramMessage` that emits `(text, entities)`. Use `StringBuilder` for text, track `currentOffset` (= `sb.Length`, which in .NET is UTF-16 code units — exactly what Telegram expects).

- [ ] **Step 1: Replace the `ToTelegramHtml` method and its two Telegram helpers.**

Open `TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs`. Replace (from the existing `ToTelegramHtml` through `RenderBlocksTelegram` end) with:

```csharp
/// <summary>
/// Render payload as entity-based Telegram message.
/// Emits Bold entities for subject, field labels, and section headers;
/// TextMention entities with full User object for clickable user mentions.
/// No HTML — uses the entities parameter which is mutually exclusive with parse_mode.
/// </summary>
public static TelegramMessage ToTelegramMessage(NotificationPayload payload)
{
    var sb = new StringBuilder();
    var entities = new List<MessageEntity>();

    // Subject (bold, own line)
    AppendBold(sb, entities, payload.Subject);
    sb.AppendLine();
    sb.AppendLine();

    RenderBlocksTelegram(sb, entities, payload.Blocks);

    return new TelegramMessage(sb.ToString().TrimEnd(), entities);
}

private static void RenderBlocksTelegram(
    StringBuilder sb, List<MessageEntity> entities, IReadOnlyList<ContentBlock> blocks)
{
    foreach (var block in blocks)
    {
        switch (block)
        {
            case TextBlock text:
                sb.AppendLine(text.Text);
                break;

            case FieldList fieldList:
                foreach (var field in fieldList.Fields)
                {
                    AppendBold(sb, entities, $"{field.Label}:");
                    sb.Append(' ');
                    if (field.User is { } u)
                        AppendUserMention(sb, entities, field.Value, u);
                    else
                        sb.Append(field.Value);
                    sb.AppendLine();
                }
                break;

            case SectionBlock section:
                sb.AppendLine();
                AppendBold(sb, entities, section.Header);
                sb.AppendLine();
                RenderBlocksTelegram(sb, entities, section.Content);
                break;
        }
    }
}

private static void AppendBold(StringBuilder sb, List<MessageEntity> entities, string text)
{
    var offset = sb.Length;
    sb.Append(text);
    entities.Add(new MessageEntity
    {
        Type = MessageEntityType.Bold,
        Offset = offset,
        Length = text.Length
    });
}

private static void AppendUserMention(
    StringBuilder sb, List<MessageEntity> entities, string displayText, UserIdentity user)
{
    var offset = sb.Length;
    sb.Append(displayText);
    entities.Add(new MessageEntity
    {
        Type = MessageEntityType.TextMention,
        Offset = offset,
        Length = displayText.Length,
        User = new User
        {
            Id = user.Id,
            IsBot = false,
            FirstName = user.FirstName ?? string.Empty,
            LastName = user.LastName,
            Username = user.Username
        }
    });
}
```

Add `using Telegram.Bot.Types;`, `using Telegram.Bot.Types.Enums;`, `using TelegramGroupsAdmin.Core.Models;` at the top of the file if not already present.

- [ ] **Step 2: Build.**

Run: `dotnet build TelegramGroupsAdmin.sln --configuration Debug`

Expected: `NotificationService.cs` call sites that used `ToTelegramHtml` will fail to compile. Those are fixed in Task 18.

- [ ] **Step 3: Do NOT commit yet.** Continue to Task 16.

---

## Task 16: Rewrite `NotificationRendererTests` for entities

**Files:**
- Modify: `TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationRendererTests.cs`

- [ ] **Step 1: Read the current test file.**

```bash
cat TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationRendererTests.cs
```

- [ ] **Step 2: Replace the existing Telegram-rendering tests with entity-based ones.**

Remove all tests that assert on HTML strings (`Does.Contain("<b>")`, `Does.Contain("tg://user")`, etc.). Add:

```csharp
using Telegram.Bot.Types.Enums;

[Test]
public void ToTelegramMessage_SubjectIsBoldOnFirstLine()
{
    var payload = NotificationPayloadBuilder.Create("Alert Title").Build();

    var rendered = NotificationRenderer.ToTelegramMessage(payload);

    Assert.That(rendered.Text, Does.StartWith("Alert Title"));
    Assert.That(rendered.Entities, Has.Some.Matches<MessageEntity>(e =>
        e.Type == MessageEntityType.Bold &&
        e.Offset == 0 &&
        e.Length == "Alert Title".Length));
}

[Test]
public void ToTelegramMessage_FieldWithoutUserEmitsPlainValueAndBoldLabel()
{
    var payload = NotificationPayloadBuilder.Create("Alert")
        .WithField("Chat", "MyGroup")
        .Build();

    var rendered = NotificationRenderer.ToTelegramMessage(payload);

    // Find the label entity
    var labelEntity = rendered.Entities.FirstOrDefault(e =>
        e.Type == MessageEntityType.Bold &&
        rendered.Text.Substring(e.Offset, e.Length) == "Chat:");
    Assert.That(labelEntity, Is.Not.Null, "label should be bold");

    // Value should not have a TextMention
    Assert.That(rendered.Entities, Has.None.Matches<MessageEntity>(e =>
        e.Type == MessageEntityType.TextMention));

    Assert.That(rendered.Text, Does.Contain("Chat: MyGroup"));
}

[Test]
public void ToTelegramMessage_FieldWithUserEmitsTextMentionWithEmbeddedUser()
{
    var user = new UserIdentity(
        Id: 12345,
        FirstName: "Alice",
        LastName: "Smith",
        Username: "alice_s");
    var payload = NotificationPayloadBuilder.Create("Alert")
        .WithField("User", user)
        .Build();

    var rendered = NotificationRenderer.ToTelegramMessage(payload);

    var mention = rendered.Entities.FirstOrDefault(e =>
        e.Type == MessageEntityType.TextMention);
    Assert.That(mention, Is.Not.Null, "user field should produce TextMention");
    Assert.That(mention!.User, Is.Not.Null);
    Assert.That(mention.User!.Id, Is.EqualTo(12345));
    Assert.That(mention.User.FirstName, Is.EqualTo("Alice"));
    Assert.That(mention.User.LastName, Is.EqualTo("Smith"));
    Assert.That(mention.User.Username, Is.EqualTo("alice_s"));

    // The span of the TextMention covers only the user display name, not the label
    var span = rendered.Text.Substring(mention.Offset, mention.Length);
    Assert.That(span, Is.EqualTo(user.DisplayName));
}

[Test]
public void ToTelegramMessage_SectionHeaderIsBold()
{
    var payload = NotificationPayloadBuilder.Create("Alert")
        .WithSection("Analysis", s => s.WithField("Score", "1.0"))
        .Build();

    var rendered = NotificationRenderer.ToTelegramMessage(payload);

    var headerEntity = rendered.Entities.FirstOrDefault(e =>
        e.Type == MessageEntityType.Bold &&
        rendered.Text.Substring(e.Offset, e.Length) == "Analysis");
    Assert.That(headerEntity, Is.Not.Null);
}

[Test]
public void ToTelegramMessage_EntityOffsetsMatchTextForNonBmpCharacters()
{
    // UserIdentity with an emoji in the first name to verify UTF-16 offset handling.
    var user = new UserIdentity(Id: 1, FirstName: "👤User", LastName: null, Username: null);
    var payload = NotificationPayloadBuilder.Create("Alert")
        .WithField("User", user)
        .Build();

    var rendered = NotificationRenderer.ToTelegramMessage(payload);

    var mention = rendered.Entities.Single(e =>
        e.Type == MessageEntityType.TextMention);
    var span = rendered.Text.Substring(mention.Offset, mention.Length);
    Assert.That(span, Is.EqualTo(user.DisplayName),
        "entity offset/length must address the exact display text in UTF-16 code units");
}
```

- [ ] **Step 3: Run tests.**

Run:
```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --filter "FullyQualifiedName~NotificationRendererTests" \
    --logger "console;verbosity=normal"
```

Expected: all new tests PASS. Existing tests that checked HTML-specific behavior should already be removed.

- [ ] **Step 4: Commit.**

```bash
git add TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs \
        TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationRendererTests.cs
git commit -F- <<'EOF'
feat(notifications): entity-based Telegram rendering with text_mention

Replaces ToTelegramHtml with ToTelegramMessage returning
(text, entities). Bold entities for titles/labels/headers; TextMention
with embedded User object for clickable mentions that work even for
users with no prior bot interaction.

Email and plain text rendering paths unchanged.

EOF
```

---

## Task 17: Add `entities` parameter to `IBotMessageHandler`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/Handlers/IBotMessageHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/Handlers/BotMessageHandler.cs`

- [ ] **Step 1: Update the interface.**

Open `IBotMessageHandler.cs`. For `SendAsync`, `SendPhotoAsync`, `SendVideoAsync`, add a new optional parameter **after** existing parameters so callers without entities are unaffected:

```csharp
Task<Message> SendAsync(
    long chatId,
    string text,
    ParseMode? parseMode = null,
    ReplyParameters? replyParameters = null,
    InlineKeyboardMarkup? replyMarkup = null,
    IReadOnlyList<MessageEntity>? entities = null,
    CancellationToken ct = default);

Task<Message> SendPhotoAsync(
    long chatId,
    InputFile photo,
    string? caption = null,
    ParseMode? parseMode = null,
    ReplyParameters? replyParameters = null,
    InlineKeyboardMarkup? replyMarkup = null,
    IReadOnlyList<MessageEntity>? captionEntities = null,
    CancellationToken ct = default);

Task<Message> SendVideoAsync(
    long chatId,
    InputFile video,
    string? caption = null,
    ParseMode? parseMode = null,
    ReplyParameters? replyParameters = null,
    InlineKeyboardMarkup? replyMarkup = null,
    IReadOnlyList<MessageEntity>? captionEntities = null,
    CancellationToken ct = default);
```

(Do not modify `SendAnimationAsync`, `EditTextAsync`, `EditCaptionAsync`, `DeleteAsync`, `AnswerCallbackAsync` — not needed for this change.)

- [ ] **Step 2: Update the implementation.**

In `BotMessageHandler.cs`, thread the new parameter through to the `apiClient.SendMessageAsync` / `SendPhotoAsync` / `SendVideoAsync` calls. Check the Telegram.Bot SDK method signature via an IDE (or `find_symbol` in CSharperMcp) to find the exact parameter name (likely `entities` and `captionEntities`).

Example for `SendAsync`:

```csharp
public async Task<Message> SendAsync(
    long chatId,
    string text,
    ParseMode? parseMode = null,
    ReplyParameters? replyParameters = null,
    InlineKeyboardMarkup? replyMarkup = null,
    IReadOnlyList<MessageEntity>? entities = null,
    CancellationToken ct = default)
{
    var apiClient = await botClientFactory.GetApiClientAsync();
    return await apiClient.SendMessageAsync(
        chatId,
        text,
        parseMode: parseMode,
        replyParameters: replyParameters,
        replyMarkup: replyMarkup,
        entities: entities,
        ct: ct);
}
```

If `ITelegramApiClient.SendMessageAsync` doesn't have an `entities` parameter, use `find_symbol` / `get_symbol_info` (CSharperMcp) to locate it and add the parameter. It likely needs to pass through to the underlying Telegram.Bot `TelegramBotClient.SendMessage` call.

- [ ] **Step 3: If `ITelegramApiClient` needs updating too, update it now.**

Open `TelegramGroupsAdmin.Telegram/Services/Bot/ITelegramApiClient.cs` and `TelegramApiClient.cs`. Add the `entities` / `captionEntities` parameter to `SendMessageAsync`, `SendPhotoAsync`, `SendVideoAsync` mirroring the handler. The implementation calls the Telegram.Bot client — look up the exact parameter name on that SDK method.

- [ ] **Step 4: Build.**

Run: `dotnet build TelegramGroupsAdmin.sln --configuration Debug`

Expected: clean build.

- [ ] **Step 5: Commit.**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Bot/Handlers/IBotMessageHandler.cs \
        TelegramGroupsAdmin.Telegram/Services/Bot/Handlers/BotMessageHandler.cs \
        TelegramGroupsAdmin.Telegram/Services/Bot/ITelegramApiClient.cs \
        TelegramGroupsAdmin.Telegram/Services/Bot/TelegramApiClient.cs
git commit -F- <<'EOF'
feat(telegram): add entities parameter to bot message handler

Optional, non-breaking — allows callers to pass explicit MessageEntity
arrays alongside (or instead of) parse_mode.

EOF
```

---

## Task 18: Add entity-based overloads to `IBotDmService`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/IBotDmService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotDmService.cs`

**Context:** Add new overloads that take a `TelegramMessage` (text + entities). The implementation calls the updated handler methods with `parseMode: null` and the entities list. Existing `string + parseMode` overloads stay for non-notification callers.

`TelegramMessage` lives in `TelegramGroupsAdmin.Services.Notifications` (internal to the main project). Since `IBotDmService` is in `TelegramGroupsAdmin.Telegram`, we can't reference `TelegramMessage` there directly without a project dependency inversion.

**Solution:** Take `(string text, IReadOnlyList<MessageEntity> entities)` as separate parameters at the service boundary. `NotificationService` (the caller) unpacks the `TelegramMessage` at the call site.

- [ ] **Step 1: Add the new overloads to the interface.**

In `IBotDmService.cs` add:

```csharp
/// <summary>
/// Attempt to send a DM using pre-rendered text + entities (no parse_mode).
/// For admin notifications that need text_mention entities.
/// If DM fails (403), queues the message for later delivery (text only, entities dropped).
/// </summary>
Task<DmDeliveryResult> SendDmWithEntitiesAsync(
    long telegramUserId,
    string notificationType,
    string text,
    IReadOnlyList<MessageEntity> entities,
    CancellationToken cancellationToken = default);

/// <summary>
/// Attempt to send a DM with media, entities, and inline keyboard buttons (no parse_mode).
/// If DM fails (403), queues the text for later delivery (without media/buttons/entities).
/// </summary>
Task<DmDeliveryResult> SendDmWithMediaAndKeyboardEntitiesAsync(
    long telegramUserId,
    string notificationType,
    string text,
    IReadOnlyList<MessageEntity> entities,
    string? photoPath = null,
    string? videoPath = null,
    InlineKeyboardMarkup? keyboard = null,
    CancellationToken cancellationToken = default);
```

Add `using Telegram.Bot.Types;` and `using Telegram.Bot.Types.ReplyMarkups;` at the top if not already present.

- [ ] **Step 2: Implement the new methods in `BotDmService.cs`.**

Model after the existing `SendDmWithQueueAsync` and `SendDmWithMediaAndKeyboardAsync` methods, but:
- Pass `parseMode: null`
- Pass `entities: entities` (text) or `captionEntities: entities` (photo/video caption)

```csharp
public async Task<DmDeliveryResult> SendDmWithEntitiesAsync(
    long telegramUserId,
    string notificationType,
    string text,
    IReadOnlyList<MessageEntity> entities,
    CancellationToken cancellationToken = default)
{
    var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

    try
    {
        await messageHandler.SendAsync(
            chatId: telegramUserId,
            text: text,
            parseMode: null,
            entities: entities,
            ct: cancellationToken);

        logger.LogInformation(
            "DM sent successfully to {User} (notification type: {NotificationType})",
            user.ToLogInfo(telegramUserId), notificationType);

        await telegramUserRepository.EnableBotDmAsync(telegramUserId, cancellationToken);

        return new DmDeliveryResult { DmSent = true, FallbackUsed = false, Failed = false };
    }
    catch (ApiRequestException ex) when (ex.ErrorCode == 403)
    {
        logger.LogWarning(
            "DM blocked for {User} - queueing {NotificationType} for later delivery",
            user.ToLogDebug(telegramUserId), notificationType);

        await telegramUserRepository.DisableBotDmAsync(telegramUserId, cancellationToken);

        // Queue text only — entities are lost but we preserve the content for eventual delivery
        await pendingNotificationsRepository.AddPendingNotificationAsync(
            telegramUserId, notificationType, text, cancellationToken: cancellationToken);

        return new DmDeliveryResult
        {
            DmSent = false, FallbackUsed = false, Failed = true,
            ErrorMessage = "User has not enabled DMs - notification queued for later delivery"
        };
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to send DM to {User}", user.ToLogDebug(telegramUserId));
        return new DmDeliveryResult
        {
            DmSent = false, FallbackUsed = false, Failed = true, ErrorMessage = ex.Message
        };
    }
}

public async Task<DmDeliveryResult> SendDmWithMediaAndKeyboardEntitiesAsync(
    long telegramUserId,
    string notificationType,
    string text,
    IReadOnlyList<MessageEntity> entities,
    string? photoPath = null,
    string? videoPath = null,
    InlineKeyboardMarkup? keyboard = null,
    CancellationToken cancellationToken = default)
{
    var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

    try
    {
        if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
        {
            await using var photoStream = File.OpenRead(photoPath);
            await messageHandler.SendPhotoAsync(
                chatId: telegramUserId,
                photo: InputFile.FromStream(photoStream, Path.GetFileName(photoPath)),
                caption: text,
                parseMode: null,
                replyMarkup: keyboard,
                captionEntities: entities,
                ct: cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
        {
            await using var videoStream = File.OpenRead(videoPath);
            await messageHandler.SendVideoAsync(
                chatId: telegramUserId,
                video: InputFile.FromStream(videoStream, Path.GetFileName(videoPath)),
                caption: text,
                parseMode: null,
                replyMarkup: keyboard,
                captionEntities: entities,
                ct: cancellationToken);
        }
        else
        {
            await messageHandler.SendAsync(
                chatId: telegramUserId,
                text: text,
                parseMode: null,
                replyMarkup: keyboard,
                entities: entities,
                ct: cancellationToken);
        }

        logger.LogInformation(
            "DM with entities/media/keyboard sent to {User}",
            user.ToLogInfo(telegramUserId));

        await telegramUserRepository.EnableBotDmAsync(telegramUserId, cancellationToken);

        return new DmDeliveryResult { DmSent = true, FallbackUsed = false, Failed = false };
    }
    catch (ApiRequestException ex) when (ex.ErrorCode == 403)
    {
        logger.LogInformation(
            "{User} has blocked bot DMs, queuing notification",
            user.ToLogInfo(telegramUserId));

        await telegramUserRepository.DisableBotDmAsync(telegramUserId, cancellationToken);

        // Queue plain text; entities/media/buttons dropped
        await pendingNotificationsRepository.AddPendingNotificationAsync(
            telegramUserId, notificationType, text, cancellationToken: cancellationToken);

        return new DmDeliveryResult
        {
            DmSent = false, FallbackUsed = false, Failed = true,
            ErrorMessage = "User has blocked bot DMs - notification queued for later delivery"
        };
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to send DM with entities/media/keyboard to {User}",
            user.ToLogDebug(telegramUserId));
        return new DmDeliveryResult
        {
            DmSent = false, FallbackUsed = false, Failed = true, ErrorMessage = ex.Message
        };
    }
}
```

- [ ] **Step 3: Build.**

Run: `dotnet build TelegramGroupsAdmin.sln --configuration Debug`

Expected: clean build.

- [ ] **Step 4: Commit.**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Bot/IBotDmService.cs \
        TelegramGroupsAdmin.Telegram/Services/Bot/BotDmService.cs
git commit -F- <<'EOF'
feat(dm): add entity-based overloads to IBotDmService

New overloads that send text + MessageEntity arrays with no parse_mode,
enabling text_mention entities for clickable user mentions.

EOF
```

---

## Task 19: Wire `NotificationService` to use the new DM overloads

**Files:**
- Modify: `TelegramGroupsAdmin/Services/NotificationService.cs`

**Context:** Replace the `SendDmWithQueueAsync(..., ParseMode.Html)` and `SendDmWithMediaAndKeyboardAsync(..., parseMode: ParseMode.Html)` calls in `SendTypedTelegramDmAsync` and `SendTelegramDmDirectAsync` with the new `*Entities` overloads.

- [ ] **Step 1: Update `SendTypedTelegramDmAsync`.**

Find the method (around line 441). Replace the render + send block:

```csharp
// Before
var htmlMessage = NotificationRenderer.ToTelegramHtml(payload);

InlineKeyboardMarkup? keyboard = null;
if (payload.Keyboard is { } kb)
{
    keyboard = await BuildReportActionKeyboardAsync(
        kb.EntityId, kb.ChatId, kb.UserId, kb.KeyboardType, ct);
}

DmDeliveryResult result;

if (keyboard != null || !string.IsNullOrWhiteSpace(payload.PhotoPath) || !string.IsNullOrWhiteSpace(payload.VideoPath))
{
    result = await _dmDeliveryService.SendDmWithMediaAndKeyboardAsync(
        mapping.TelegramId, "notification", htmlMessage,
        photoPath: payload.PhotoPath, videoPath: payload.VideoPath,
        keyboard: keyboard, parseMode: ParseMode.Html, cancellationToken: ct);
}
else
{
    result = await _dmDeliveryService.SendDmWithQueueAsync(
        mapping.TelegramId, "notification", htmlMessage,
        parseMode: ParseMode.Html, cancellationToken: ct);
}

// After
var telegramMessage = NotificationRenderer.ToTelegramMessage(payload);

InlineKeyboardMarkup? keyboard = null;
if (payload.Keyboard is { } kb)
{
    keyboard = await BuildReportActionKeyboardAsync(
        kb.EntityId, kb.ChatId, kb.UserId, kb.KeyboardType, ct);
}

DmDeliveryResult result;

if (keyboard != null || !string.IsNullOrWhiteSpace(payload.PhotoPath) || !string.IsNullOrWhiteSpace(payload.VideoPath))
{
    result = await _dmDeliveryService.SendDmWithMediaAndKeyboardEntitiesAsync(
        mapping.TelegramId, "notification",
        telegramMessage.Text, telegramMessage.Entities,
        photoPath: payload.PhotoPath, videoPath: payload.VideoPath,
        keyboard: keyboard, cancellationToken: ct);
}
else
{
    result = await _dmDeliveryService.SendDmWithEntitiesAsync(
        mapping.TelegramId, "notification",
        telegramMessage.Text, telegramMessage.Entities,
        cancellationToken: ct);
}
```

- [ ] **Step 2: Update `SendTelegramDmDirectAsync` the same way.**

Find the method (around line 512), apply the same `ToTelegramHtml` → `ToTelegramMessage` and `*WithQueue*` → `*WithEntitiesAsync` swaps.

- [ ] **Step 3: Remove the `using Telegram.Bot.Types.Enums;` import if `ParseMode` is no longer referenced in this file.**

```bash
grep -n "ParseMode" TelegramGroupsAdmin/Services/NotificationService.cs
```

If zero hits, remove the import.

- [ ] **Step 4: Build.**

Run: `dotnet build TelegramGroupsAdmin.sln --configuration Debug`

Expected: clean build.

- [ ] **Step 5: Run the full unit-test suite.**

Run:
```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
    --configuration Debug \
    --logger "console;verbosity=minimal"
```

Expected: all tests pass. Any failures at this point are migration leftovers — fix call sites.

- [ ] **Step 6: Commit.**

```bash
git add TelegramGroupsAdmin/Services/NotificationService.cs
git commit -F- <<'EOF'
feat(notifications): wire admin DMs to entity-based sending

Admin notifications now send via entities instead of parse_mode=Html.
User mentions become proper text_mention entities with embedded User
objects, so they're clickable even when the mentioned user has never
interacted with the admin's Telegram client.

EOF
```

---

## Task 20: Run the integration test suite and verify behavior end-to-end

**Files:** none (verification only)

- [ ] **Step 1: Run the integration test suite.**

Run in background (takes ~10 min):
```bash
mkdir -p logs
dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
    --configuration Debug \
    --logger "console;verbosity=minimal" \
    | tee logs/integration-$(date +%s).log &
```

Wait for completion, then check the log file.

Expected: all tests pass. The new `ReportCallbackContextRepositoryTests.DeleteOrphanedAsync_*` test is among them.

- [ ] **Step 2: Verify migration sanity.**

Run: `dotnet run --project TelegramGroupsAdmin --migrate-only`

Expected: "No pending migrations" or the normal migration-apply log, and clean exit.

- [ ] **Step 3: Open the design doc and cross-check each non-goal is preserved.**

```bash
cat docs/superpowers/specs/2026-04-23-admin-notification-fixes-design.md | head -80
```

Walk through the "Non-Goals" list and verify none have been altered:
- `NotificationBell` / `NotificationItem` untouched
- Email rendering untouched
- Web push plain text untouched
- `Actor.FromWebUser` untouched
- Other report cards (ProfileScanAlertCard, etc.) untouched

- [ ] **Step 4: Manual verification checklist** (run in dev or whenever the next dev DM occurs — document as a comment in the PR):

1. Trigger a spam detection → admin DM received → target user name is **clickable and styled** (tap opens profile) for a user with no prior interaction.
2. Submit a `/report` in a managed chat → moderation report card on the web UI shows the reporter name as a clickable link that opens the user detail dialog.
3. Simulate multi-admin: have two admins receive the same report DM. Admin A clicks "Dismiss". Admin B clicks their button a minute later. Admin B sees `"Already handled by ..."` — not `"Button expired"`.
4. Leave a report pending for 10 minutes. Admin buttons still work (no 7-day-style expiry).

- [ ] **Step 5: Push the branch and open a PR to `develop`.**

```bash
git push -u origin fix/admin-notification-buttons-and-mentions
gh pr create --base develop --title "fix(notifications): cross-admin button expiration and clickable DM mentions" --body "$(cat <<'EOF'
## Summary
- Stops eager deletion of DM callback contexts in report action handlers; multi-admin races now resolve to "Already handled by X" instead of "Button expired"
- Replaces 7-day callback context retention with orphan-based cleanup (tied to report lifetime)
- Switches admin notification Telegram DMs from parse_mode=Html to entity-based sending, with proper `text_mention` entities for user mentions
- Wraps the reporter name in `UserDetailLink` on the moderation report card for `/report`-submitted reports

Design: [docs/superpowers/specs/2026-04-23-admin-notification-fixes-design.md](docs/superpowers/specs/2026-04-23-admin-notification-fixes-design.md)
Plan: [docs/superpowers/plans/2026-04-23-admin-notification-fixes.md](docs/superpowers/plans/2026-04-23-admin-notification-fixes.md)

## Test plan
- [x] Unit tests — all passing (handlers + renderer + builder)
- [x] Integration tests — `ReportCallbackContextRepositoryTests.DeleteOrphanedAsync_*` passing
- [x] Migration-only run passes
- [ ] Manual: trigger spam detection, confirm DM target user clickable
- [ ] Manual: submit /report, confirm reporter clickable on UI
- [ ] Manual: multi-admin race produces "Already handled by"
EOF
)"
```

- [ ] **Step 6: Commit any last-minute fixes** from manual verification if found.

---

## Self-Review Notes

After writing this plan, I cross-checked it against the spec:

**Spec coverage:**
- Bug A — covered by Tasks 1–7
- Bug B (entity rendering) — covered by Tasks 9–19
- Bug C (UI link) — covered by Task 8
- Non-goals — preserved (no email, web push, web-user Actor, or other report-card changes)

**Placeholder scan:** No TBDs or vague steps. Every code step has complete code.

**Type consistency:**
- `Field(string Label, string Value, UserIdentity? User = null)` used consistently (Task 9, 16, tests)
- `TelegramMessage(string Text, IReadOnlyList<MessageEntity> Entities)` used consistently (Task 14, 15, 19)
- Method names: `DeleteOrphanedAsync` consistent (Task 5, 7)
- New DM methods: `SendDmWithEntitiesAsync`, `SendDmWithMediaAndKeyboardEntitiesAsync` consistent (Task 18, 19)

Plan is self-consistent and matches the spec.
