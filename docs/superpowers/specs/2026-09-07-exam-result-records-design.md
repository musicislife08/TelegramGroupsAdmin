# Exam Result Records + Auto-Admit Notification — Design

**Date:** 2026-09-07
**Issues:** Closes #515 — the issue is taken over as the tracker for both parts; part 2
(auto-admit notification) is new scope from this brainstorm, delivered in the same PR.

## Summary

Today only **failed** entrance exams leave a trace: a `ReportType.ExamFailure` row in the
`reports` table plus an admin notification with Approve / Deny / Deny & Ban buttons. When a
user **passes**, `ExamFlowService.EvaluateAndCompleteAsync` deletes the exam session and admits
the user without persisting anything — the open-ended answer, MC answers, and AI reasoning are
irreversibly destroyed (#515).

This design:

1. **Part 1 — durable record for every completed exam.** Generalize the exam report type so
   passes are persisted too, born in a completed state, reviewable in the Reports UI.
2. **Part 2 — admin notification on auto-admit.** When a user is admitted because the exam
   graded them as passed, notify admins with a keyboard whose actions override the automatic
   decision (Deny / Deny & Ban) or acknowledge it (Dismiss).

## Decisions (from brainstorm)

- **All passes get a record** — AI-evaluated and MC-only alike. Fully closes the #515 data-loss hole.
- **Approach: generalize, don't add.** One report type (`ExamResult`) with an outcome field,
  not a sibling `ExamPass` type. Clean rename, no `[Obsolete]`, no aliases.
- **Retention:** passes inherit the existing reports purge (`DeleteOldReportsAsync`). No special casing.
- **Notification scope:** exam auto-passes only. Non-exam welcome admissions stay silent.
- **Pass keyboard replaces Approve with Dismiss** — approving an already-admitted user is
  meaningless; the remaining actions *are* the override.
- **JSONB enums serialize as ints** — no `JsonStringEnumConverter`; human readability of the
  context blob is a non-goal.

## Invariants

- **A failed exam is never in limbo.** Failure records accept exactly Approve / Deny /
  Deny & Ban — each resolves the user's fate. Dismiss is **pass-only** and must be rejected
  (callback router and web UI alike) when aimed at a `Failed` record.
- **The exam record is written before the session is deleted.** The `exam_sessions` row is the
  only source of the raw answers; persistence must precede `DeleteSessionAsync`.
- **First admin action wins, atomically.** Concurrent clicks resolve via a guarded
  `ExecuteUpdate`; the loser gets the existing "already handled by X" message.
- **Zero behavior change to the failure flow** beyond mechanical renames.

## Part 1 — Data model & write path

### Rename (mechanical, no wire change)

| Today | Becomes |
|---|---|
| `ReportType.ExamFailure = 2` | `ReportType.ExamResult = 2` (same value; `type` column untouched) |
| `ExamFailureRecord` | `ExamResultRecord` |
| `ExamFailureContext` | `ExamResultContext` |
| `IReportsRepository.InsertExamFailureAsync` / `GetExamFailureAsync` / `GetExamFailuresAsync` | `InsertExamResultAsync` / `GetExamResultAsync` / `GetExamResultsAsync` |
| `IExamFlowService.ApproveExamFailureAsync` / `DenyExamFailureAsync` / `DenyAndBanExamFailureAsync` | `ApproveExamResultAsync` / `DenyExamResultAsync` / `DenyAndBanExamResultAsync` |

All call sites, tests, and UI references rename with them. One type per file throughout.

**String-value audit (verified 2026-09-07):** nothing depends on the enum's *name*. `reports.type`
and `report_callback_contexts.report_type` are `short` columns; there is no `Enum.Parse`/`TryParse`
of `ReportType`, no `"ExamFailure"` string literal, no `ReportType.ToString()`; `BackupService`'s
`JsonStringEnumConverter` only sees `ReportDto.Type` which is already `short`. The rename touches
compiled identifiers only.

### New: `ExamOutcome`

```csharp
public enum ExamOutcome
{
    Failed = 0,
    Passed = 1
}
```

- Own file in `TelegramGroupsAdmin.Core/Models/`.
- Added to `ExamResultContext` as `[JsonPropertyName("outcome")]` serialized as **int**.
- Added to `ExamResultRecord` as `Outcome`.

### Migration

One EF migration whose `Up` stamps existing exam rows' JSONB context via raw SQL:

```sql
UPDATE reports SET context = jsonb_set(context, '{outcome}', '0')
WHERE type = 2 AND context IS NOT NULL;
```

(All pre-existing exam rows are failures by definition.) No schema change — `outcome` lives in
the JSONB context. Fluent API config unchanged.

### Born-state per outcome

| | Fail (unchanged) | Pass (new) |
|---|---|---|
| `Status` | `Pending` | `Reviewed` |
| `ReviewedBy` | — | `Actor.ExamFlow` display text |
| `ActionTaken` | — | `"auto-approved"` |
| `ReviewedAt` | — | now |
| `AdminNotes` | — | AI reasoning summary (when present) |

Passes never appear in the pending queue; they are visible under "All Statuses".

### Write path (`ExamFlowService.EvaluateAndCompleteAsync`)

Reorder:

1. Compute MC score + AI evaluation (unchanged).
2. Build `ExamResultRecord` from the session — MC answers, shuffle state, open-ended answer,
   score/threshold, AI reasoning, `Outcome` — shared by both branches (the fail branch loses its
   duplicate record-building).
3. `InsertExamResultAsync` — **before** `DeleteSessionAsync`.
4. `DeleteSessionAsync`.
5. Branch: pass → `ExecuteExamApprovalAsync` + pass notification (Part 2);
   fail → failure notification + pending DM (unchanged).

Edge cases:

- **AI unavailable** → forced to review, exactly as today: `Outcome = Failed`, `Pending`.
- **Pass held for profile review** (admission gate defers): still recorded `Passed` /
  auto-approved — the exam did pass; the profile hold is a separate pipeline with its own
  `ProfileScanAlert` report.

## Part 2 — Auto-admit notification & override

### Event + send

- New `NotificationEventType.ExamPassed` — per-admin opt-in via existing notification settings,
  same plumbing as `ExamFailed`.
- New `INotificationService.SendExamPassNotificationAsync`, called from the pass branch after
  insert (the report id must exist for the keyboard). Payload mirrors the failure notification:
  title "User Auto-Admitted (Passed Exam)", user, chat, MC score vs threshold, open-ended Q/A,
  AI reasoning — via `NotificationPayloadBuilder` + `ActionKeyboardContext`.

### Keyboard

`ExamAction` gains `Dismiss = 3`.

```
Fail (unchanged):                 Pass (new):
[ ✓ Approve ]  [ ✗ Deny ]         [ ✓ Dismiss ]  [ ✗ Deny ]
[ 🚫 Deny & Ban ]                 [ 🚫 Deny & Ban ]
```

Both outcomes share `ReportType.ExamResult`, so `NotificationService.BuildReportActionKeyboardAsync`
needs the outcome to pick the keyboard — `ActionKeyboardContext` carries it (or an equivalent
overload).

### Callback handling

`TryUpdateStatusAsync` guards on `status = Pending`, so a born-`Reviewed` pass record can never
be stamped through it. New repository method:

```csharp
Task<bool> TryOverrideAutoDecisionAsync(
    long reportId, string reviewedBy, string actionTaken, string? notes,
    CancellationToken cancellationToken);
// ExecuteUpdate WHERE id = @id AND action_taken = 'auto-approved'
```

First click flips `action_taken` off the sentinel atomically → wins; second click loses the
race → existing "already handled by {ReviewedBy}" formatting. `Status` stays `Reviewed`.

`ExamHandler` / `RouteExamAsync`:

- Bounds check widens to `ExamAction.Dismiss`.
- Handler fetches the record and routes by `Outcome`:
  - `Failed` + Approve/Deny/DenyAndBan → existing paths, untouched.
  - `Failed` + Dismiss → **rejected** (invariant: a failure must be resolved, never dismissed).
  - `Passed` + Dismiss → no Telegram action; override stamp only
    (`ActionTaken = "dismissed (auto-admit acknowledged)"`).
  - `Passed` + Deny / DenyAndBan → existing `DenyExamFailureAsync` / `DenyAndBanExamFailureAsync`
    flows (kick / kick+ban the admitted user, welcome response → Denied, DM), with the status
    stamp routed through `TryOverrideAutoDecisionAsync`
    (`ActionTaken = "deny (override auto-approval)"` / `"deny+ban (override auto-approval)"`).
  - `Passed` + Approve → **rejected** (stale-keyboard safety; nothing to approve).

### Audit logging

`ContentReportHandler` logs `AuditEventType.ReportReviewed` for every action; `ExamHandler`
currently logs nothing — an existing gap. Every exam review action gains an audit event after
its status stamp succeeds, following the `ContentReportHandler` pattern
(`IAuditService.LogEventAsync(AuditEventType.ReportReviewed, executor, target, details)`):

- Failure actions: `"Approved after exam failure (exam #id)"`, `"Denied entry — kicked (exam #id)"`,
  `"Denied entry — banned (exam #id)"`.
- Override actions call out the override explicitly:
  `"Overrode exam auto-approval — denied/kicked (exam #id)"`,
  `"Overrode exam auto-approval — denied & banned (exam #id)"`,
  `"Dismissed exam auto-admit notification (exam #id)"`.

The audit event is written only when the action actually won the race (no audit rows for
"already handled" losers). No new `AuditEventType` value — `ReportReviewed` already means
"report reviewed and actioned".

## UI (Reports page)

- Filter label "Exam Reviews" now covers both outcomes (type unchanged); default
  "Pending Only" filter naturally hides passes.
- `ExamReviewCard` receives the outcome:
  - Passed: "Passed — auto-admitted" success chip; action buttons Dismiss / Deny / Deny & Ban,
    driven through the same `ExamHandler` override semantics as the Telegram keyboard.
  - Failed: unchanged (Approve / Deny / Deny & Ban; **no Dismiss rendered**).

## Testing

- **Unit** — `ExamFlowServiceTests`: pass writes the record before session delete; born-state
  fields correct per outcome; AI-unavailable still yields `Failed`/`Pending`.
  `ExamHandlerTests`: Dismiss on pass; Deny/DenyAndBan override on pass; Dismiss on failure
  rejected; Approve on pass rejected; override race → "already handled"; every winning action
  logs a `ReportReviewed` audit event (override wording for pass actions) and race losers log
  nothing.
- **Integration** — `ReportsRepositoryTests`: `TryOverrideAutoDecisionAsync` atomicity (two
  concurrent overrides, one winner); outcome round-trips as int in JSONB. Unit: a context
  missing the `outcome` key (only possible after restoring a pre-outcome backup — live rows
  are always stamped by the migration) deserializes as `Failed`.
- **E2E** — extend `ExamReportsTests` / `ExamFlowE2ETests`: passing the exam produces a
  completed report visible under "All Statuses" with correct content; existing failure tests
  keep passing (renames only).

## Out of scope

- Notifications for non-exam automatic admissions (welcome/captcha-only flows).
- A dedicated exam-history page or per-user profile exam-history view (possible follow-up once
  records exist).
- Any change to exam grading logic, thresholds, or the AI evaluation prompt.
