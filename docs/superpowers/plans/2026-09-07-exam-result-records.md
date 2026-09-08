# Exam Result Records + Auto-Admit Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist a durable record for every completed entrance exam (pass and fail), and notify admins on auto-admit with a Dismiss / Deny / Deny & Ban override keyboard, with audit events for every review action.

**Architecture:** Generalize the existing `ReportType.ExamFailure` report machinery into `ExamResult` with an int-serialized `ExamOutcome` in the JSONB context. Passes are inserted born-completed (`Reviewed`, auto-approved by the ExamFlow actor) before the exam session is deleted; overrides flip the auto-approved sentinel atomically via a new guarded `ExecuteUpdate`. All exam review actions gain `ReportReviewed` audit events.

**Tech Stack:** .NET 10, EF Core 10 + PostgreSQL (JSONB), Blazor Server + MudBlazor 9, NUnit + NSubstitute 6, bUnit component tests, Telegram.Bot inline keyboards.

**Spec:** `docs/superpowers/specs/2026-09-07-exam-result-records-design.md`

**Branch:** `feat/515-exam-result-records` (already created; PR targets `develop`, body starts with `Closes #515`).

## Global Constraints

- Never commit to `master`/`develop`; conventional commits; heredoc for multi-line commit messages (`git commit -F- <<'EOF'`).
- One type per file — every new class/record/enum gets its own file named after it.
- No tuples crossing a method boundary (parameters, returns, event callbacks) — use named records there; tuples local to a method body are fine (this plan converts the exam card's tuple callback while touching it).
- No `[Obsolete]`, no backward-compat aliases — clean renames.
- JSONB enums serialize as **ints** — never add `JsonStringEnumConverter` to exam context serialization.
- EF Core: modify models + `AppDbContext` first, then `dotnet ef migrations add <Name> -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`. Apply locally with `dotnet run --migrate-only` (from `TelegramGroupsAdmin/`).
- NSubstitute 6 matcher lambdas: `Arg.Is<T>(x => x!.Prop == y)` — null-forgiving `!` on first dereference, never `?.`.
- Build: `dotnet build TelegramGroupsAdmin.sln`. Unit tests: `dotnet test TelegramGroupsAdmin.UnitTests`. Component: `dotnet test TelegramGroupsAdmin.ComponentTests`. Integration: `dotnet test TelegramGroupsAdmin.IntegrationTests` (needs Docker/Postgres — see `E2E_TESTING.md`). E2E: `dotnet test TelegramGroupsAdmin.E2ETests`.
- Do not touch files under `TelegramGroupsAdmin.Data/Migrations/` except the one new migration; historical migrations keep old names.

---

### Task 1: Mechanical rename — `ExamFailure` → `ExamResult` (no behavior change)

Everything that says "exam failure" but means "a completed exam" renames. The solution must compile and the full existing test suite must pass unchanged — this task adds zero behavior. The spec's string-value audit confirmed nothing depends on enum names (DB columns are `short`; no `Enum.Parse`, no name literals, backups serialize the short DTO field), so this touches compiled identifiers only.

**Files:**
- Rename: `TelegramGroupsAdmin.Core/Models/ExamFailureRecord.cs` → `ExamResultRecord.cs` (type + filename)
- Rename: `TelegramGroupsAdmin.Core/Models/ExamFailureContext.cs` → `ExamResultContext.cs`
- Rename: `TelegramGroupsAdmin.E2ETests/Infrastructure/TestExamFailureBuilder.cs` → `TestExamResultBuilder.cs`
- Modify: `TelegramGroupsAdmin.Core/Models/ReportType.cs` (enum member)
- Modify: `TelegramGroupsAdmin.Core/Repositories/IReportsRepository.cs`, `ReportsRepository.cs` (method names)
- Modify: `TelegramGroupsAdmin.Core/Repositories/Mappings/EnrichedReportMappings.cs` (`ToExamFailure` → `ToExamResult`)
- Modify: `TelegramGroupsAdmin.Telegram/Services/IExamFlowService.cs`, `ExamFlowService.cs` (method names)
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs`, `ReportCallbackService.cs`
- Modify: `TelegramGroupsAdmin/Services/NotificationService.cs` (keyboard switch case, `ActionKeyboardContext` usage, `examFailureId` param)
- Modify: `TelegramGroupsAdmin.Core/Services/INotificationService.cs` (`examFailureId` param name)
- Modify: `TelegramGroupsAdmin/Components/Pages/Reports.razor`, `TelegramGroupsAdmin/Components/Reports/ExamReviewCard.razor`
- Modify: all test files referencing renamed symbols (`ExamHandlerTests`, `ExamFlowServiceTests` ×2, `ReportsRepositoryTests`, `ExamReportsTests`, `ExamFlowE2ETests`, `ExamReviewCardTests`, …)

**Interfaces (produced — later tasks rely on these exact names):**
- `ReportType.ExamResult = 2`
- `record ExamResultRecord` with `CompletedAt` (was `FailedAt`)
- `record ExamResultContext`
- `IReportsRepository.InsertExamResultAsync(ExamResultRecord, CancellationToken)` / `GetExamResultAsync(long, CancellationToken)` / `GetExamResultsAsync(long?, bool, CancellationToken)`
- `IExamFlowService.ApproveExamResultAsync(...)` / `DenyExamResultAsync(...)` / `DenyAndBanExamResultAsync(...)` (parameter `examFailureId` → `examResultId`)
- `EnrichedReportMappings.ToExamResult(this EnrichedReportView)`
- `TestExamResultBuilder` (E2E)

- [ ] **Step 1: Perform the rename sweep**

Rename map (whole-word, case-sensitive):

| Old | New |
|---|---|
| `ReportType.ExamFailure` | `ReportType.ExamResult` |
| `ExamFailureRecord` | `ExamResultRecord` |
| `ExamFailureContext` | `ExamResultContext` |
| `InsertExamFailureAsync` | `InsertExamResultAsync` |
| `GetExamFailureAsync` | `GetExamResultAsync` |
| `GetExamFailuresAsync` | `GetExamResultsAsync` |
| `ApproveExamFailureAsync` | `ApproveExamResultAsync` |
| `DenyExamFailureAsync` | `DenyExamResultAsync` |
| `DenyAndBanExamFailureAsync` | `DenyAndBanExamResultAsync` |
| `ToExamFailure` | `ToExamResult` |
| `TestExamFailureBuilder` | `TestExamResultBuilder` |
| `examFailureId` / `ExamFailureId` | `examResultId` / `ExamResultId` |
| `ExamResultRecord.FailedAt` property | `CompletedAt` ("when the exam completed" — true for both outcomes) |

Also rename local variables / parameters typed as the record (`examFailure` → `examResult`, `ExamFailure` component parameter → `ExamResult`, `Reports.razor`'s `ReportTypeFilter.ExamFailure` → `ReportTypeFilter.ExamResult`, `ReportQueueItem.ExamFailure` property → `ExamResult`, `_pendingExamCount` sources). Update XML doc comments that say "exam failure" where the thing now means any completed exam (e.g. `ReportType.ExamResult` doc: `/// <summary>Completed entrance exam (pass or fail); failures await admin decision</summary>`). Keep genuinely failure-specific wording (e.g. `SendExamFailureNotificationAsync` **keeps its name** — it fires only for failures; only its `examFailureId` parameter renames).

Use `mcp__csharp-er-mcp__find_symbol_usages` (after `initialize_workspace` on `TelegramGroupsAdmin.sln`) to enumerate call sites per symbol, or a careful `grep -rln` sweep. Do NOT touch `TelegramGroupsAdmin.Data/Migrations/*` or `docs/`.

- [ ] **Step 2: Verify no stragglers**

Run:
```bash
grep -rn "ExamFailure" --include='*.cs' --include='*.razor' . | grep -v obj/ | grep -v /Migrations/ | grep -v "SendExamFailureNotificationAsync"
```
Expected: zero lines.

- [ ] **Step 3: Build and run unit + component tests**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.ComponentTests`
Expected: build clean, all tests PASS (behavior unchanged).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor: rename ExamFailure report machinery to ExamResult

Mechanical rename only (enum member keeps wire value 2; DB stores
shorts, nothing depends on the name). Prepares #515: passed exams will
share this record type with an outcome field.
EOF
```

---

### Task 2: `ExamOutcome` + born-state insert + stamp migration

**Files:**
- Create: `TelegramGroupsAdmin.Core/Models/ExamOutcome.cs`
- Modify: `TelegramGroupsAdmin.Core/Models/ExamResultContext.cs`
- Modify: `TelegramGroupsAdmin.Core/Models/ExamResultRecord.cs`
- Modify: `TelegramGroupsAdmin.Core/Repositories/ReportsRepository.cs` (`InsertExamResultAsync`, ~line 599)
- Modify: `TelegramGroupsAdmin.Core/Repositories/Mappings/EnrichedReportMappings.cs` (`ToExamResult`)
- Create: migration `StampExamResultOutcome` (generated, then edited)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Models/ExamResultContextTests.cs` (new)
- Test: `TelegramGroupsAdmin.IntegrationTests/ContentDetection/Repositories/ReportsRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1 names.
- Produces: `enum ExamOutcome { Failed = 0, Passed = 1 }`; `ExamResultContext.Outcome`; `ExamResultRecord.Outcome`; `ExamResultRecord.AutoApprovedActionTaken` (`const string`, value `"auto-approved"`); `InsertExamResultAsync` writes outcome-dependent born-state.

- [ ] **Step 1: Write failing serialization tests**

`TelegramGroupsAdmin.UnitTests/Telegram/Models/ExamResultContextTests.cs`:

```csharp
using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Models;

[TestFixture]
public class ExamResultContextTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Test]
    public void Outcome_SerializesAsInt()
    {
        var context = new ExamResultContext { UserId = 1, Outcome = ExamOutcome.Passed };

        var json = JsonSerializer.Serialize(context, JsonOptions);

        Assert.That(json, Does.Contain("\"outcome\":1"));
        Assert.That(json, Does.Not.Contain("Passed"));
    }

    [Test]
    public void Outcome_MissingAfterPreOutcomeBackupRestore_DeserializesAsFailed()
    {
        // The migration stamps every live row, so this key is never absent through the
        // normal path. It CAN be absent after restoring a backup taken before ExamOutcome
        // existed: BackupService re-inserts DTO rows verbatim and migrations don't re-run
        // on restore. Every exam row from that era is a failure, so the enum default (0)
        // must be Failed.
        const string preOutcomeBackupJson = """{"userId":42,"score":50,"passingThreshold":80}""";

        var context = JsonSerializer.Deserialize<ExamResultContext>(preOutcomeBackupJson, JsonOptions);

        Assert.That(context!.Outcome, Is.EqualTo(ExamOutcome.Failed));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter ExamResultContextTests`
Expected: FAIL — `ExamResultContext` has no `Outcome` member (compile error is the failure here; that's fine).

- [ ] **Step 3: Add the enum and fields**

`TelegramGroupsAdmin.Core/Models/ExamOutcome.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Final outcome of a completed entrance exam.
/// Serialized as int in the report JSONB context — never as a name.
/// </summary>
public enum ExamOutcome
{
    /// <summary>Exam failed (or AI unavailable) — pending admin review</summary>
    Failed = 0,

    /// <summary>Exam passed — user auto-admitted, record born completed</summary>
    Passed = 1
}
```

`ExamResultContext` — add after `AiEvaluation`:

```csharp
    /// <summary>
    /// Final exam outcome. Serializes as int (0=Failed, 1=Passed).
    /// Live rows are always stamped (migration); the key can only be absent after restoring
    /// a backup taken before ExamOutcome existed — every row from that era is a failure,
    /// so the enum default (0 = Failed) is the correct fallback.
    /// </summary>
    [JsonPropertyName("outcome")]
    public ExamOutcome Outcome { get; init; }
```

`ExamResultRecord` — add:

```csharp
    /// <summary>ActionTaken sentinel for a pass record no human has touched yet.</summary>
    public const string AutoApprovedActionTaken = "auto-approved";

    /// <summary>Final exam outcome (from JSONB context).</summary>
    public ExamOutcome Outcome { get; init; }
```

- [ ] **Step 4: Run serialization tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter ExamResultContextTests`
Expected: PASS (System.Text.Json serializes enums as numbers by default — no converter added anywhere).

- [ ] **Step 5: Write failing integration test for born-state**

Add to `ReportsRepositoryTests` (follow the file's existing fixture pattern for repo construction; adapt an existing exam insert test as the template):

```csharp
[Test]
public async Task InsertExamResultAsync_PassedOutcome_BornCompleted()
{
    var record = new ExamResultRecord
    {
        User = new UserIdentity(12345, "Pat", null, "pat"),
        Chat = ChatIdentity.FromId(-100123),
        Outcome = ExamOutcome.Passed,
        Score = 100,
        PassingThreshold = 80,
        AiEvaluation = "Genuine interest, on-topic answer",
        CompletedAt = DateTimeOffset.UtcNow
    };

    var id = await _repository.InsertExamResultAsync(record);

    var stored = await _repository.GetExamResultAsync(id);
    Assert.That(stored, Is.Not.Null);
    Assert.That(stored!.Outcome, Is.EqualTo(ExamOutcome.Passed));
    Assert.That(stored.ReviewedAt, Is.Not.Null, "pass records are born completed");
    Assert.That(stored.ActionTaken, Is.EqualTo(ExamResultRecord.AutoApprovedActionTaken));
    Assert.That(stored.ReviewedBy, Is.EqualTo(Actor.ExamFlow.GetDisplayText()));
    Assert.That(stored.AdminNotes, Is.EqualTo("Genuine interest, on-topic answer"));
}

[Test]
public async Task InsertExamResultAsync_FailedOutcome_BornPending()
{
    var record = new ExamResultRecord
    {
        User = new UserIdentity(12346, "Sam", null, "sam"),
        Chat = ChatIdentity.FromId(-100123),
        Outcome = ExamOutcome.Failed,
        Score = 20,
        PassingThreshold = 80,
        CompletedAt = DateTimeOffset.UtcNow
    };

    var id = await _repository.InsertExamResultAsync(record);

    var stored = await _repository.GetExamResultAsync(id);
    Assert.That(stored!.Outcome, Is.EqualTo(ExamOutcome.Failed));
    Assert.That(stored.ReviewedAt, Is.Null, "failures stay pending, unchanged");
    Assert.That(stored.ActionTaken, Is.Null);
}
```

(`GetExamResultAsync` reads through the `EnrichedReports` view — if the view join yields no user row for the random test user, follow the pattern existing exam tests use to seed user data.)

- [ ] **Step 6: Run to verify the pass-record assertions fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "InsertExamResultAsync_PassedOutcome_BornCompleted|InsertExamResultAsync_FailedOutcome_BornPending"`
Expected: `BornCompleted` FAILS (`ReviewedAt` null, `Outcome` not persisted); `BornPending` may fail on `Outcome` until Step 7.

- [ ] **Step 7: Implement born-state insert + mapping**

`ReportsRepository.InsertExamResultAsync` — write outcome into the context and branch the born-state:

```csharp
        var examContext = new ExamResultContext
        {
            UserId = examResult.User.Id,
            McAnswers = examResult.McAnswers,
            ShuffleState = examResult.ShuffleState,
            OpenEndedAnswer = examResult.OpenEndedAnswer,
            Score = examResult.Score,
            PassingThreshold = examResult.PassingThreshold,
            AiEvaluation = examResult.AiEvaluation,
            Outcome = examResult.Outcome
        };

        var isPass = examResult.Outcome == ExamOutcome.Passed;
        var entity = new ReportDto
        {
            Type = (short)ReportType.ExamResult,
            ChatId = examResult.Chat.Id,
            ReportedAt = examResult.CompletedAt,
            Status = (int)(isPass ? ReportStatus.Reviewed : ReportStatus.Pending),
            ReviewedBy = isPass ? Actor.ExamFlow.GetDisplayText() : null,
            ActionTaken = isPass ? ExamResultRecord.AutoApprovedActionTaken : null,
            ReviewedAt = isPass ? DateTimeOffset.UtcNow : null,
            AdminNotes = isPass ? examResult.AiEvaluation : null,
            Context = JsonSerializer.Serialize(examContext, JsonOptions)
        };
```

(`Actor.GetDisplayText()` is the same extension `ReportStatusHelper` uses — check its namespace via `Actor` usages there.) Update the log line to include the outcome. In `EnrichedReportMappings.ToExamResult`, map `Outcome = examContext.Outcome` and `CompletedAt = view.ReportedAt`.

- [ ] **Step 8: Run integration tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter ReportsRepositoryTests`
Expected: PASS (new tests and all pre-existing exam tests).

- [ ] **Step 9: Create the stamp migration**

No model/schema change (outcome lives inside the existing JSONB `context`), so the generated migration will be empty — it exists to stamp legacy rows:

```bash
cd TelegramGroupsAdmin && dotnet ef migrations add StampExamResultOutcome -p ../TelegramGroupsAdmin.Data -s .
```

Edit the generated `Up` (leave `Down` empty — removing the key would destroy real pass data if rolled back after new rows exist; a stamp of `0` on failures is the pre-migration semantic anyway):

```csharp
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // All exam rows created before ExamOutcome existed are failures (0).
        // Idempotent: only stamps rows missing the key.
        migrationBuilder.Sql(
            """
            UPDATE reports
            SET context = jsonb_set(context, '{outcome}', '0')
            WHERE type = 2
              AND context IS NOT NULL
              AND NOT (context ? 'outcome');
            """);
    }
```

- [ ] **Step 10: Apply migration locally and verify**

Run: `cd TelegramGroupsAdmin && dotnet run --migrate-only`
Expected: exits cleanly, migration applied.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat: add ExamOutcome to exam result records with born-state insert

Passes are born completed (Reviewed, auto-approved sentinel, ExamFlow
reviewer, AI reasoning in notes); failures born Pending exactly as
before. Outcome serializes as int in JSONB; legacy rows stamped 0
(Failed) by migration.

Part of #515.
EOF
```

---

### Task 3: `TryOverrideAutoDecisionAsync` repository method

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Repositories/IReportsRepository.cs`
- Modify: `TelegramGroupsAdmin.Core/Repositories/ReportsRepository.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/ContentDetection/Repositories/ReportsRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 2 (`ExamResultRecord.AutoApprovedActionTaken`, born-state insert).
- Produces:

```csharp
Task<bool> TryOverrideAutoDecisionAsync(
    long reportId,
    string reviewedBy,
    string actionTaken,
    string? notes = null,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write failing integration tests**

```csharp
[Test]
public async Task TryOverrideAutoDecisionAsync_AutoApprovedRecord_FirstCallWinsSecondLoses()
{
    var id = await InsertPassedExamAsync(); // helper: insert ExamResultRecord with Outcome=Passed (as in Task 2 test)

    var first = await _repository.TryOverrideAutoDecisionAsync(
        id, "admin@test.com", "deny (override auto-approval)", "kicked");
    var second = await _repository.TryOverrideAutoDecisionAsync(
        id, "other@test.com", "dismissed (auto-admit acknowledged)");

    Assert.That(first, Is.True);
    Assert.That(second, Is.False, "sentinel already consumed — race loser");

    var stored = await _repository.GetExamResultAsync(id);
    Assert.That(stored!.ReviewedBy, Is.EqualTo("admin@test.com"));
    Assert.That(stored.ActionTaken, Is.EqualTo("deny (override auto-approval)"));
}

[Test]
public async Task TryOverrideAutoDecisionAsync_PendingFailure_DoesNotMatch()
{
    var id = await InsertFailedExamAsync(); // helper: Outcome=Failed → born Pending, ActionTaken null

    var result = await _repository.TryOverrideAutoDecisionAsync(
        id, "admin@test.com", "deny (override auto-approval)");

    Assert.That(result, Is.False, "only the auto-approved sentinel is overridable");
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter TryOverrideAutoDecisionAsync`
Expected: FAIL — method not defined (compile error).

- [ ] **Step 3: Implement**

Interface doc + method in `IReportsRepository` (next to `TryUpdateStatusAsync`); implementation in `ReportsRepository`:

```csharp
    public async Task<bool> TryOverrideAutoDecisionAsync(
        long reportId,
        string reviewedBy,
        string actionTaken,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Atomic guard on the auto-approved sentinel: the first admin action wins,
        // concurrent clicks lose the race and surface "already handled".
        // Status stays Reviewed — the record was born completed.
        var rowsAffected = await context.Reports
            .Where(r => r.Id == reportId
                && r.Type == (short)ReportType.ExamResult
                && r.ActionTaken == ExamResultRecord.AutoApprovedActionTaken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ReviewedBy, reviewedBy)
                .SetProperty(r => r.ActionTaken, actionTaken)
                .SetProperty(r => r.ReviewedAt, DateTimeOffset.UtcNow)
                .SetProperty(r => r.AdminNotes, notes),
                cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Overrode auto-decision on exam report {ReportId} by {ReviewedBy} (action: {ActionTaken})",
                reportId, reviewedBy, actionTaken);
        }

        return rowsAffected > 0;
    }
```

- [ ] **Step 4: Run integration tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter ReportsRepositoryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat: atomic override of auto-approved exam decisions

ExecuteUpdate guarded on the auto-approved ActionTaken sentinel; first
admin action wins, concurrent clicks lose the race.

Part of #515.
EOF
```

---

### Task 4: `ExamAction.Dismiss`, outcome-aware `ExamHandler`, audit events, callback routing

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Constants/ExamAction.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/IExamHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/IReportActionsService.cs` + its implementation (`HandleExamDismissAsync`, mirroring `HandleExamApproveAsync`'s delegation)
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportCallbackService.cs` (`RouteExamAsync`)
- Test: `TelegramGroupsAdmin.UnitTests/Services/ReportActions/ExamHandlerTests.cs`

**Interfaces:**
- Consumes: Task 3 `TryOverrideAutoDecisionAsync`; Task 2 `ExamOutcome`, `AutoApprovedActionTaken`; `IAuditService.LogEventAsync(AuditEventType, Actor, Actor?, string?, CancellationToken)` (exists in Core).
- Produces: `ExamAction.Dismiss = 3`; `IExamHandler.DismissAsync(long examId, Actor executor, CancellationToken)`; `IReportActionsService.HandleExamDismissAsync(long examId, Actor executor, CancellationToken cancellationToken = default)`. Every winning exam action logs `AuditEventType.ReportReviewed`.

Behavior matrix (from spec — the invariant is that a **failed exam is never dismissible** and always resolvable):

| Record | Approve | Deny | Deny & Ban | Dismiss |
|---|---|---|---|---|
| Failed (Pending) | existing flow + audit | existing flow + audit | existing flow + audit | **rejected** |
| Passed (auto-approved) | **rejected** | kick via `DenyExamResultAsync` + override stamp + audit | ban via `DenyAndBanExamResultAsync` + override stamp + audit | override stamp only + audit |
| Passed (already overridden) | already handled | already handled | already handled | already handled |

- [ ] **Step 1: Write failing handler tests**

Add to `ExamHandlerTests` (constructor gains `IAuditService`; update `SetUp` — `_mockAuditService = Substitute.For<IAuditService>();` and pass it to `new ExamHandler(...)`; add a `CreateTestPassedExam()` helper mirroring `CreateTestExam()` with `Outcome = ExamOutcome.Passed, ActionTaken = ExamResultRecord.AutoApprovedActionTaken, ReviewedAt = DateTimeOffset.UtcNow, ReviewedBy = "Exam Flow"`; default `_mockReportsRepo.TryOverrideAutoDecisionAsync(...)` to `Returns(true)`):

```csharp
[Test]
public async Task DismissAsync_PassedRecord_StampsOverrideWithoutModerationAction()
{
    var exam = CreateTestPassedExam();
    _mockReportsRepo.GetExamResultAsync(TestExamId, Arg.Any<CancellationToken>()).Returns(exam);

    var result = await _handler.DismissAsync(TestExamId, TestExecutor, CancellationToken.None);

    Assert.That(result.Success, Is.True);
    await _mockReportsRepo.Received(1).TryOverrideAutoDecisionAsync(
        TestExamId, Arg.Any<string>(),
        Arg.Is<string>(a => a!.StartsWith("dismissed")), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    await _mockExamFlowService.DidNotReceiveWithAnyArgs().DenyExamResultAsync(default!, default!, default!, default, default);
    await _mockAuditService.Received(1).LogEventAsync(
        AuditEventType.ReportReviewed, TestExecutor, Arg.Any<Actor?>(),
        Arg.Is<string>(v => v!.Contains("Dismissed")), Arg.Any<CancellationToken>());
}

[Test]
public async Task DismissAsync_FailedRecord_Rejected()
{
    var exam = CreateTestExam(); // Outcome = Failed, pending
    _mockReportsRepo.GetExamResultAsync(TestExamId, Arg.Any<CancellationToken>()).Returns(exam);

    var result = await _handler.DismissAsync(TestExamId, TestExecutor, CancellationToken.None);

    Assert.That(result.Success, Is.False, "a failed exam must be resolved, never dismissed");
    await _mockReportsRepo.DidNotReceiveWithAnyArgs()
        .TryOverrideAutoDecisionAsync(default, default!, default!, default, default);
    await _mockAuditService.DidNotReceiveWithAnyArgs()
        .LogEventAsync(default, default!, default, default, default);
}

[Test]
public async Task ApproveAsync_PassedRecord_Rejected()
{
    var exam = CreateTestPassedExam();
    _mockReportsRepo.GetExamResultAsync(TestExamId, Arg.Any<CancellationToken>()).Returns(exam);

    var result = await _handler.ApproveAsync(TestExamId, TestExecutor, CancellationToken.None);

    Assert.That(result.Success, Is.False, "nothing to approve on an auto-approved pass");
    await _mockExamFlowService.DidNotReceiveWithAnyArgs().ApproveExamResultAsync(default!, default!, default, default!, default);
}

[Test]
public async Task DenyAsync_PassedRecord_KicksAndStampsOverride()
{
    var exam = CreateTestPassedExam();
    _mockReportsRepo.GetExamResultAsync(TestExamId, Arg.Any<CancellationToken>()).Returns(exam);
    _mockExamFlowService.DenyExamResultAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
            Arg.Any<long?>(), Arg.Any<CancellationToken>())
        .Returns(new ModerationResult { Success = true });

    var result = await _handler.DenyAsync(TestExamId, TestExecutor, CancellationToken.None);

    Assert.That(result.Success, Is.True);
    await _mockReportsRepo.Received(1).TryOverrideAutoDecisionAsync(
        TestExamId, Arg.Any<string>(),
        Arg.Is<string>(a => a!.Contains("override")), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    await _mockReportsRepo.DidNotReceiveWithAnyArgs().TryUpdateStatusAsync(
        default, default, default!, default!, default, default);
    await _mockAuditService.Received(1).LogEventAsync(
        AuditEventType.ReportReviewed, TestExecutor, Arg.Any<Actor?>(),
        Arg.Is<string>(v => v!.Contains("Overrode")), Arg.Any<CancellationToken>());
}

[Test]
public async Task DenyAsync_PassedRecord_RaceLost_ReturnsAlreadyHandledAndNoAudit()
{
    var exam = CreateTestPassedExam();
    _mockReportsRepo.GetExamResultAsync(TestExamId, Arg.Any<CancellationToken>()).Returns(exam);
    _mockExamFlowService.DenyExamResultAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
            Arg.Any<long?>(), Arg.Any<CancellationToken>())
        .Returns(new ModerationResult { Success = true });
    _mockReportsRepo.TryOverrideAutoDecisionAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
        .Returns(false);

    var result = await _handler.DenyAsync(TestExamId, TestExecutor, CancellationToken.None);

    Assert.That(result.Success, Is.False);
    await _mockAuditService.DidNotReceiveWithAnyArgs()
        .LogEventAsync(default, default!, default, default, default);
}

[Test]
public async Task ApproveAsync_FailedRecord_LogsAuditEvent()
{
    var exam = CreateTestExam();
    _mockReportsRepo.GetExamResultAsync(TestExamId, Arg.Any<CancellationToken>()).Returns(exam);
    _mockExamFlowService.ApproveExamResultAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), TestExamId,
            Arg.Any<Actor>(), Arg.Any<CancellationToken>())
        .Returns(new ModerationResult { Success = true });

    await _handler.ApproveAsync(TestExamId, TestExecutor, CancellationToken.None);

    await _mockAuditService.Received(1).LogEventAsync(
        AuditEventType.ReportReviewed, TestExecutor, Arg.Any<Actor?>(),
        Arg.Is<string>(v => v!.Contains($"exam #{TestExamId}")), Arg.Any<CancellationToken>());
}
```

Also update `FetchExamAsync`-dependent expectations: `CreateTestPassedExam()` records have `ReviewedAt` set, so existing `CheckAlreadyHandled` logic would short-circuit them — Step 3 makes the fetch outcome-aware; the tests above encode the target behavior.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter ExamHandlerTests`
Expected: FAIL (no `Dismiss` member, no `DismissAsync`, no audit service in constructor).

- [ ] **Step 3: Implement**

`ExamAction.cs` — add:

```csharp
    /// <summary>Acknowledge an auto-admit notification (pass records only; a failed exam is never dismissible)</summary>
    Dismiss = 3
```

`ExamHandler`:
- Constructor gains `IAuditService auditService` (primary-constructor parameter alongside the existing ones; DI resolves it — `IAuditService` is already registered).
- `FetchExamAsync`: an untouched auto-approved pass is actionable, not "already handled":

```csharp
    private async Task<FetchResult<ExamResultRecord>> FetchExamAsync(long examId, CancellationToken cancellationToken)
    {
        var exam = await reportsRepository.GetExamResultAsync(examId, cancellationToken);
        if (exam == null)
            return FetchResult<ExamResultRecord>.Fail($"Exam result {examId} not found");

        // A pass still carrying the auto-approved sentinel awaits a possible human
        // override — its ReviewedAt/ReviewedBy reflect the system, not an admin.
        var isUntouchedAutoApproval = exam.Outcome == ExamOutcome.Passed
            && exam.ActionTaken == ExamResultRecord.AutoApprovedActionTaken;
        if (!isUntouchedAutoApproval)
        {
            var alreadyHandled = ReportStatusHelper.CheckAlreadyHandled(
                exam.ReviewedBy, exam.ActionTaken, exam.ReviewedAt);
            if (alreadyHandled != null)
                return FetchResult<ExamResultRecord>.Handled(alreadyHandled.Message);
        }

        return FetchResult<ExamResultRecord>.Ok(exam);
    }
```

- `ApproveAsync`: after fetch, reject passes:

```csharp
        if (exam.Outcome == ExamOutcome.Passed)
            return new ReviewActionResult(false, "User was auto-admitted — use Dismiss to acknowledge or Deny to override");
```

- `DenyAsync` / `DenyAndBanAsync`: after the moderation call succeeds, branch the stamp. Extract a private helper so both use it:

```csharp
    private async Task<ReviewActionResult?> TryStampAsync(
        ExamResultRecord exam, long examId, Actor executor,
        string failAction, string failNotes,
        string overrideAction, string? overrideNotes,
        CancellationToken cancellationToken)
    {
        if (exam.Outcome == ExamOutcome.Passed)
        {
            var overridden = await reportsRepository.TryOverrideAutoDecisionAsync(
                examId, executor.GetDisplayText(), overrideAction, overrideNotes, cancellationToken);
            if (overridden)
                return null;

            var current = await reportsRepository.GetExamResultAsync(examId, cancellationToken);
            return ReportStatusHelper.FormatAlreadyHandled(
                current?.ReviewedBy, current?.ActionTaken, current?.ReviewedAt);
        }

        return await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, examId, ReportStatus.Reviewed, executor, failAction, failNotes,
            async () =>
            {
                var current = await reportsRepository.GetExamResultAsync(examId, cancellationToken);
                return current != null
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedBy, current.ActionTaken, current.ReviewedAt)
                    : new ReviewActionResult(false, $"Exam result {examId} could not be updated");
            },
            cancellationToken);
    }
```

Call sites (`null` return = stamp won → proceed to audit + success result):

```csharp
        // DenyAsync
        var statusResult = await TryStampAsync(exam, examId, executor,
            failAction: "deny", failNotes: "Denied entry after exam review",
            overrideAction: "deny (override auto-approval)", overrideNotes: "User kicked",
            cancellationToken);
        if (statusResult != null) return statusResult;

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, Actor.FromUserIdentity(exam.User),
            exam.Outcome == ExamOutcome.Passed
                ? $"Overrode exam auto-approval — denied/kicked (exam #{examId})"
                : $"Denied entry — kicked (exam #{examId})",
            cancellationToken);
```

`DenyAndBanAsync` mirrors it with `failAction: "deny-and-ban"` (keep whatever string the current code uses — read it before editing), `overrideAction: "deny+ban (override auto-approval)"`, `overrideNotes: "User banned"`, and audit values `$"Overrode exam auto-approval — denied & banned (exam #{examId})"` / `$"Denied entry — banned (exam #{examId})"`. `ApproveAsync` (failures only, after its existing stamp) logs `$"Approved after exam failure (exam #{examId})"`.

- New `DismissAsync`:

```csharp
    public async Task<ReviewActionResult> DismissAsync(long examId, Actor executor, CancellationToken cancellationToken)
    {
        var fetch = await FetchExamAsync(examId, cancellationToken);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var exam = fetch.Value!;

        // Invariant: a failed exam must be resolved (approve/deny) — never dismissed into limbo.
        if (exam.Outcome != ExamOutcome.Passed)
            return new ReviewActionResult(false, "A failed exam cannot be dismissed — approve or deny it");

        var overridden = await reportsRepository.TryOverrideAutoDecisionAsync(
            examId, executor.GetDisplayText(), "dismissed (auto-admit acknowledged)",
            notes: null, cancellationToken);
        if (!overridden)
        {
            var current = await reportsRepository.GetExamResultAsync(examId, cancellationToken);
            return ReportStatusHelper.FormatAlreadyHandled(
                current?.ReviewedBy, current?.ActionTaken, current?.ReviewedAt);
        }

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, Actor.FromUserIdentity(exam.User),
            $"Dismissed exam auto-admit notification (exam #{examId})", cancellationToken);

        logger.LogInformation("Exam review {ExamId}: auto-admit dismissed by {Executor}",
            examId, executor.DisplayName);

        return new ReviewActionResult(true, "Auto-admit acknowledged", ActionName: "Dismiss");
    }
```

Add `DismissAsync` to `IExamHandler`. Add to `IReportActionsService` + implementation (delegate to `examHandler.DismissAsync`, mirroring how `HandleExamApproveAsync` delegates):

```csharp
    Task<ReviewActionResult> HandleExamDismissAsync(long examId, Actor executor, CancellationToken cancellationToken = default);
```

`ReportCallbackService.RouteExamAsync`:

```csharp
        if (actionInt < 0 || actionInt > (int)ExamAction.Dismiss)
            return new ReviewActionResult(false, "Invalid action");

        return (ExamAction)actionInt switch
        {
            ExamAction.Approve => await reportActionsService.HandleExamApproveAsync(reviewId, executor, cancellationToken: cancellationToken),
            ExamAction.Deny => await reportActionsService.HandleExamDenyAsync(reviewId, executor, cancellationToken: cancellationToken),
            ExamAction.DenyAndBan => await reportActionsService.HandleExamDenyAndBanAsync(reviewId, executor, cancellationToken: cancellationToken),
            ExamAction.Dismiss => await reportActionsService.HandleExamDismissAsync(reviewId, executor, cancellationToken: cancellationToken),
            _ => new ReviewActionResult(false, "Unknown action")
        };
```

- [ ] **Step 4: Run unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "ExamHandlerTests|ReportCallbackService"`
Expected: PASS — new tests and all existing ones (existing tests updated for the new constructor arg only).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat: exam Dismiss action, auto-approval overrides, review audit events

ExamHandler routes by outcome: passes accept Dismiss/Deny/DenyAndBan
through the atomic override guard (Approve rejected); failures keep
Approve/Deny/DenyAndBan and can never be dismissed. Every winning
action logs a ReportReviewed audit event; race losers log nothing.

Part of #515.
EOF
```

---

### Task 5: `ExamPassed` notification with override keyboard

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Models/NotificationEventType.cs`
- Modify: `TelegramGroupsAdmin.Core/Services/INotificationService.cs`
- Modify: `TelegramGroupsAdmin/Services/NotificationService.cs`
- Modify: `TelegramGroupsAdmin/Services/Notifications/ActionKeyboardContext.cs`
- Modify: `TelegramGroupsAdmin/Components/Shared/NotificationPreferencesCard.razor`
- Test: `TelegramGroupsAdmin.UnitTests` — if `NotificationService` has existing keyboard/payload tests, extend them; otherwise the keyboard shape is covered indirectly and by E2E (Task 7's component tests cover the web side).

**Interfaces:**
- Consumes: Task 2 `ExamOutcome`; Task 4 `ExamAction.Dismiss`.
- Produces:

```csharp
// NotificationEventType — append after ProfileScanAlert:
ExamPassed // Notify admins when a user passes the entrance exam and is auto-admitted

// INotificationService:
Task<Dictionary<string, bool>> SendExamPassNotificationAsync(
    ChatIdentity chat,
    UserIdentity user,
    int mcCorrectCount,
    int mcTotal,
    int mcScore,
    int mcPassingThreshold,
    string? openEndedQuestion,
    string? openEndedAnswer,
    string? aiReasoning,
    long examResultId,
    CancellationToken ct = default);

// ActionKeyboardContext gains an optional outcome:
internal sealed record ActionKeyboardContext(
    long EntityId,
    long ChatId,
    long UserId,
    ReportType KeyboardType,
    ExamOutcome? Outcome = null);
```

- [ ] **Step 1: Add the enum member and interface method** (as above — `ExamPassed` appended last so existing stored preference values are untouched).

- [ ] **Step 2: Implement `SendExamPassNotificationAsync`**

In `NotificationService`, next to `SendExamFailureNotificationAsync` (~line 180), same shape:

```csharp
    public Task<Dictionary<string, bool>> SendExamPassNotificationAsync(
        ChatIdentity chat,
        UserIdentity user,
        int mcCorrectCount,
        int mcTotal,
        int mcScore,
        int mcPassingThreshold,
        string? openEndedQuestion,
        string? openEndedAnswer,
        string? aiReasoning,
        long examResultId,
        CancellationToken ct = default)
    {
        var payload = NotificationPayloadBuilder.Create("User Auto-Admitted: Passed Entrance Exam")
            .WithField("User", user)
            .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
            .WithSection("Results", s =>
            {
                if (mcTotal > 0)
                {
                    s.WithField("Answered", $"{mcCorrectCount}/{mcTotal} correct");
                    s.WithField("Score", $"{mcScore}% (Required: {mcPassingThreshold}%)");
                }
            })
            .WithSection("Open-Ended Response", s =>
            {
                if (openEndedQuestion != null) s.WithField("Question", openEndedQuestion);
                if (openEndedAnswer != null) s.WithField("Answer", openEndedAnswer);
                if (aiReasoning != null) s.WithField("AI Reasoning", aiReasoning);
            })
            .WithKeyboard(new ActionKeyboardContext(examResultId, chat.Id, user.Id, ReportType.ExamResult, ExamOutcome.Passed))
            .Build();

        return SendToChatAudienceAsync(chat, NotificationEventType.ExamPassed, payload, ct);
    }
```

- [ ] **Step 3: Thread the outcome into the keyboard builder**

- `DispatchEntityDmAsync` (~line 550): pass `kb.Outcome` through.
- `BuildReportActionKeyboardAsync` (~line 631): add parameter `ExamOutcome? examOutcome`, and replace the `ReportType.ExamResult` case:

```csharp
            ReportType.ExamResult when examOutcome == ExamOutcome.Passed => new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✓ Dismiss", $"rev:{contextId}:{(int)ExamAction.Dismiss}"),
                    InlineKeyboardButton.WithCallbackData("✗ Deny", $"rev:{contextId}:{(int)ExamAction.Deny}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚫 Deny & Ban", $"rev:{contextId}:{(int)ExamAction.DenyAndBan}")
                }
            }),
            ReportType.ExamResult => new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✓ Approve", $"rev:{contextId}:{(int)ExamAction.Approve}"),
                    InlineKeyboardButton.WithCallbackData("✗ Deny", $"rev:{contextId}:{(int)ExamAction.Deny}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚫 Deny & Ban", $"rev:{contextId}:{(int)ExamAction.DenyAndBan}")
                }
            }),
```

(The failure keyboard — no Dismiss — enforces the invariant at the keyboard level too.)

- [ ] **Step 4: Preference labels**

`NotificationPreferencesCard.razor` — add to `GetEventDisplayName`:

```csharp
        NotificationEventType.ExamFailed => "Exam Failed (Needs Review)",
        NotificationEventType.ExamPassed => "Exam Passed (Auto-Admitted)",
```

and to `GetEventColor`: `NotificationEventType.ExamFailed => Color.Warning,` / `NotificationEventType.ExamPassed => Color.Success,` (before the fallbacks; ExamFailed currently renders via `ToString()` — fixing its label is a one-line courtesy while here).

- [ ] **Step 5: Build + run unit/component tests**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.ComponentTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat: ExamPassed admin notification with override keyboard

Auto-admit notification mirrors the failure one; keyboard swaps Approve
for Dismiss (Dismiss/Deny/Deny&Ban) — the failure keyboard never gains
Dismiss.

Part of #515.
EOF
```

---

### Task 6: Persist passes in `ExamFlowService` (write-before-delete) + send notification

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/ExamFlowService.cs` (`EvaluateAndCompleteAsync`, ~lines 433–582)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/ExamFlowServiceTests.cs`

**Interfaces:**
- Consumes: Task 2 `InsertExamResultAsync` born-state; Task 5 `SendExamPassNotificationAsync`.
- Produces: every completed exam persists an `ExamResultRecord` before its session row is deleted.

- [ ] **Step 1: Write failing unit tests**

Follow the existing `ExamFlowServiceTests` fixture pattern (it substitutes the scoped services the flow resolves — reuse its existing setup helpers for session/config/repos). Target behaviors:

```csharp
[Test]
public async Task EvaluateAndComplete_Passed_PersistsPassedRecordBeforeSessionDelete()
{
    // Arrange a session + config where the exam passes (use the fixture's existing
    // pass-scenario setup — e.g. MC score above threshold, no open-ended question).

    // Act: drive the final answer through the fixture's usual entry point.

    Received.InOrder(() =>
    {
        _reportsRepo.InsertExamResultAsync(
            Arg.Is<ExamResultRecord>(r => r!.Outcome == ExamOutcome.Passed),
            Arg.Any<CancellationToken>());
        _sessionRepo.DeleteSessionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    });
    await _notificationService.Received(1).SendExamPassNotificationAsync(
        Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(),
        Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
        Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
        Arg.Any<long>(), Arg.Any<CancellationToken>());
}

[Test]
public async Task EvaluateAndComplete_Failed_PersistsFailedRecordBeforeSessionDelete()
{
    // Arrange the fixture's existing fail scenario.

    Received.InOrder(() =>
    {
        _reportsRepo.InsertExamResultAsync(
            Arg.Is<ExamResultRecord>(r => r!.Outcome == ExamOutcome.Failed),
            Arg.Any<CancellationToken>());
        _sessionRepo.DeleteSessionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    });
    await _notificationService.DidNotReceiveWithAnyArgs().SendExamPassNotificationAsync(
        default!, default!, default, default, default, default,
        default, default, default, default, default);
}

[Test]
public async Task EvaluateAndComplete_AiUnavailable_PersistsFailedPendingRecord()
{
    // Arrange the fixture's existing AI-unavailable scenario (open-ended question
    // configured, EvaluateAnswerAsync substitute returns null) — forced to review.

    await _reportsRepo.Received(1).InsertExamResultAsync(
        Arg.Is<ExamResultRecord>(r => r!.Outcome == ExamOutcome.Failed),
        Arg.Any<CancellationToken>());
    await _notificationService.DidNotReceiveWithAnyArgs().SendExamPassNotificationAsync(
        default!, default!, default, default, default, default,
        default, default, default, default, default);
}
```

(Adapt substitute field names to the fixture's; the assertions — insert-before-delete, outcome value, which notification fires — are the contract.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter ExamFlowServiceTests`
Expected: new tests FAIL (pass path inserts nothing today; insert currently happens after delete on the fail path).

- [ ] **Step 3: Restructure `EvaluateAndCompleteAsync`**

Replace the block from `// Delete session` through the end of the fail-branch record building with:

```csharp
        var outcome = passed ? ExamOutcome.Passed : ExamOutcome.Failed;

        // Persist the exam record BEFORE deleting the session — the session row is
        // the only source of the raw answers (#515: never destroy them unrecorded).
        var examResult = new ExamResultRecord
        {
            User = UserIdentity.From(user),
            Chat = ChatIdentity.FromId(session.ChatId),
            McAnswers = session.McAnswers,
            ShuffleState = session.ShuffleState,
            OpenEndedAnswer = session.OpenEndedAnswer,
            Score = mcScore,
            PassingThreshold = examConfig.McPassingThreshold,
            AiEvaluation = aiReasoning,
            Outcome = outcome,
            CompletedAt = DateTimeOffset.UtcNow
        };

        var examResultId = await reportsRepo.InsertExamResultAsync(examResult, cancellationToken);

        await sessionRepo.DeleteSessionAsync(session.Id, cancellationToken);
```

Then the `if (passed)` branch keeps `ExecuteExamApprovalAsync` as-is and, after it, sends the pass notification (resolve `IManagedChatsRepository` + `INotificationService` from the already-open scope, same as the fail branch does):

```csharp
            var passChat = await scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>()
                .GetByChatIdAsync(session.ChatId, cancellationToken);
            var passNotificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            await passNotificationService.SendExamPassNotificationAsync(
                chat: passChat?.Identity ?? ChatIdentity.FromId(session.ChatId),
                user: UserIdentity.From(user),
                mcCorrectCount: mcCorrectCount,
                mcTotal: examConfig.McQuestions.Count,
                mcScore: mcScore,
                mcPassingThreshold: examConfig.McPassingThreshold,
                openEndedQuestion: examConfig.OpenEndedQuestion,
                openEndedAnswer: session.OpenEndedAnswer,
                aiReasoning: aiReasoning,
                examResultId: examResultId,
                ct: cancellationToken);
```

The fail branch drops its now-duplicate record construction/insert and uses `examResultId` for `SendExamFailureNotificationAsync`. Everything else in the fail branch (notification fields, pending DM, log) stays.

- [ ] **Step 4: Run the full unit suite**

Run: `dotnet test TelegramGroupsAdmin.UnitTests`
Expected: PASS (existing fail-path tests still green — insert moved earlier but still happens with the same data).

- [ ] **Step 5: Run integration flow tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter ExamFlowServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat: persist every completed exam before session delete; notify on pass

Pass and fail now share one record build inserted ahead of
DeleteSessionAsync — passed users' answers are no longer destroyed.
Auto-admits fire the ExamPassed notification with the override keyboard.

Part of #515.
EOF
```

---

### Task 7: Web UI — override state on `ExamReviewCard` + `Reports.razor` dismiss

**Files:**
- Create: `TelegramGroupsAdmin/Components/Reports/ExamCardAction.cs`
- Modify: `TelegramGroupsAdmin/Components/Reports/ExamReviewCard.razor`
- Modify: `TelegramGroupsAdmin/Components/Pages/Reports.razor`
- Test: `TelegramGroupsAdmin.ComponentTests/Components/ExamReviewCardTests.cs`

**Interfaces:**
- Consumes: Task 4 `HandleExamDismissAsync`; Task 2 `ExamOutcome` / `AutoApprovedActionTaken`.
- Produces: `public sealed record ExamCardAction(ExamResultRecord ExamResult, ExamAction Action);` — the card's `OnAction` becomes `EventCallback<ExamCardAction>` (repo rule: records, not tuples).

- [ ] **Step 1: Write failing component tests**

Add to `ExamReviewCardTests` (follow the file's existing bUnit render pattern and record builders):

```csharp
[Test]
public void PassedRecord_AutoApproved_ShowsDismissDenyDenyBan_NoApprove()
{
    var record = CreateExamResult() with
    {
        Outcome = ExamOutcome.Passed,
        ReviewedAt = DateTimeOffset.UtcNow,
        ReviewedBy = "Exam Flow",
        ActionTaken = ExamResultRecord.AutoApprovedActionTaken
    };

    var cut = RenderCard(record);

    var buttons = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
    Assert.That(buttons, Does.Contain("Dismiss"));
    Assert.That(buttons, Does.Contain("Deny"));
    Assert.That(buttons, Does.Contain("Deny + Ban"));
    Assert.That(buttons, Does.Not.Contain("Approve"));
}

[Test]
public void FailedRecord_Pending_NeverShowsDismiss()
{
    var record = CreateExamResult() with { Outcome = ExamOutcome.Failed };

    var cut = RenderCard(record);

    var buttons = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
    Assert.That(buttons, Does.Contain("Approve"));
    Assert.That(buttons, Does.Not.Contain("Dismiss"), "a failed exam is never dismissible");
}

[Test]
public void PassedRecord_AlreadyOverridden_ShowsActionTextOnly()
{
    var record = CreateExamResult() with
    {
        Outcome = ExamOutcome.Passed,
        ReviewedAt = DateTimeOffset.UtcNow,
        ReviewedBy = "admin@test.com",
        ActionTaken = "dismissed (auto-admit acknowledged)"
    };

    var cut = RenderCard(record);

    Assert.That(cut.FindAll("button").Select(b => b.TextContent.Trim()),
        Does.Not.Contain("Dismiss").And.Not.Contain("Deny"));
    Assert.That(cut.Markup, Does.Contain("dismissed (auto-admit acknowledged)"));
}
```

(`CreateExamResult()` / `RenderCard()` = the file's existing helpers, adapted; if it builds records inline, mirror that instead.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter ExamReviewCardTests`
Expected: new tests FAIL.

- [ ] **Step 3: Implement card changes**

`ExamCardAction.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Components.Reports;

/// <summary>An admin's chosen action on an exam review card.</summary>
public sealed record ExamCardAction(ExamResultRecord ExamResult, ExamAction Action);
```

`ExamReviewCard.razor`:
- `[Parameter] public EventCallback<ExamCardAction> OnAction { get; set; }` and the four handlers invoke `OnAction.InvokeAsync(new ExamCardAction(ExamResult, ExamAction.X))`; add `OnDismiss` mirroring `OnDeny` with `ExamAction.Dismiss`.
- Header area: when `ExamResult.Outcome == ExamOutcome.Passed`, render
  `<MudChip T="string" Size="Size.Small" Color="Color.Success">Passed — auto-admitted</MudChip>`.
- Replace the `<MudCardActions>` gate:

```razor
    <MudCardActions>
        @if (ExamResult.Outcome == ExamOutcome.Passed
             && ExamResult.ActionTaken == ExamResultRecord.AutoApprovedActionTaken)
        {
            <MudButton Variant="Variant.Filled" Color="Color.Success"
                       StartIcon="@Icons.Material.Filled.Check"
                       OnClick="@OnDismiss" Disabled="@_actionInProgress">Dismiss</MudButton>
            <MudButton Variant="Variant.Filled" Color="Color.Warning"
                       StartIcon="@Icons.Material.Filled.Close"
                       OnClick="@OnDeny" Disabled="@_actionInProgress">Deny</MudButton>
            <MudButton Variant="Variant.Filled" Color="Color.Error"
                       StartIcon="@Icons.Material.Filled.Block"
                       OnClick="@OnDenyAndBan" Disabled="@_actionInProgress">Deny + Ban</MudButton>
        }
        else if (!ExamResult.ReviewedAt.HasValue)
        {
            @* existing Approve / Deny / Deny + Ban buttons, unchanged *@
        }
        else
        {
            <MudText Typo="Typo.body2" Color="Color.Secondary">
                Action: <b>@ExamResult.ActionTaken</b>
            </MudText>
        }
    </MudCardActions>
```

`Reports.razor`:
- `HandleExamAction(ExamCardAction args)` — switch gains
  `ExamAction.Dismiss => await ReportActionsService.HandleExamDismissAsync(args.ExamResult.Id, executor, cancellationToken: _cancellationTokenSource.Token),`
- Exam queue item: `Timestamp = examResult.CompletedAt`; leave `IsPending = !examResult.ReviewedAt.HasValue` (passes are born reviewed → hidden under "Pending Only", exactly the spec's completed-state behavior).

- [ ] **Step 4: Run component tests**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(ui): exam pass records with override actions in Reports

Passed cards show a success chip and Dismiss/Deny/Deny+Ban override
buttons while the auto-approved sentinel stands; failure cards are
unchanged and never render Dismiss. Card callback tuple replaced with
ExamCardAction record.

Part of #515.
EOF
```

---

### Task 8: E2E coverage + full verification

**Files:**
- Modify: `TelegramGroupsAdmin.E2ETests/Infrastructure/TestExamResultBuilder.cs`
- Modify: `TelegramGroupsAdmin.E2ETests/Tests/Reports/ExamReportsTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: E2E proof that a passed exam yields a completed, reviewable report.

- [ ] **Step 1: Extend the builder**

`TestExamResultBuilder` gains outcome support (it inserts via `IReportsRepository.InsertExamResultAsync`, so born-state comes free):

```csharp
    private ExamOutcome _outcome = ExamOutcome.Failed;

    public TestExamResultBuilder AsPassed()
    {
        _outcome = ExamOutcome.Passed;
        _score = 100;
        return this;
    }
```

and passes `Outcome = _outcome` into the record it builds (`_failedAt` field renamed `_completedAt` in Task 1).

- [ ] **Step 2: Write the E2E test**

Add to `ExamReportsTests` (follow its existing login/navigation helpers):

```csharp
[Test]
public async Task PassedExam_VisibleUnderAllStatuses_WithAutoAdmittedChip()
{
    await new TestExamResultBuilder(Services)
        .AsPassed()
        .WithOpenEndedAnswer("I love self-hosting and want to compare notes")
        .WithAiEvaluation("Genuine, on-topic answer")
        .BuildAsync();

    // Navigate to /reports, set type filter "Exam Reviews", status filter "All Statuses"
    // (reuse the file's existing filter helpers), then:
    //  - the exam card is visible with the "Passed — auto-admitted" chip
    //  - the card shows Dismiss / Deny / Deny + Ban buttons and no Approve
    //  - switching status filter to "Pending Only" hides the card
}
```

Fill the navigation/assertion body with the file's established Playwright patterns — assert on the chip text `Passed — auto-admitted`, button texts, and card absence under Pending Only.

- [ ] **Step 3: Run E2E exam suites**

Run: `dotnet test TelegramGroupsAdmin.E2ETests --filter "ExamReportsTests|ExamFlowE2ETests"`
Expected: PASS (including pre-existing failure-path E2E, proving no regression).

- [ ] **Step 4: Full verification sweep**

```bash
dotnet build TelegramGroupsAdmin.sln
dotnet test TelegramGroupsAdmin.UnitTests
dotnet test TelegramGroupsAdmin.ComponentTests
dotnet test TelegramGroupsAdmin.IntegrationTests
dotnet test TelegramGroupsAdmin.E2ETests
```
Expected: all PASS. Do not claim completion without this output.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
test(e2e): passed exams surface as completed reports with override actions

Part of #515.
EOF
```

- [ ] **Step 6: Finish the branch**

Use superpowers:finishing-a-development-branch. PR: `feat/515-exam-result-records` → `develop`, body starting with `Closes #515`. Reminder from repo memory: `Closes #N` never auto-fires here (PRs target develop, not the default branch) — close #515 manually after merge; merge with **merge commit**, not squash.
