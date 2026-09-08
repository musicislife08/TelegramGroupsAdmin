using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Extensions;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

/// <summary>
/// Handles exam result review actions (approve, deny, deny-and-ban, dismiss).
/// Routes by ExamOutcome: a failed exam always resolves via approve/deny/deny-and-ban
/// (never dismissible — the invariant is a failed exam can never be left in limbo); a
/// passed (auto-approved) exam accepts dismiss/deny/deny-and-ban through the atomic
/// override guard, and rejects approve (nothing to approve on an already-admitted user).
/// Delegates to IExamFlowService for actual exam operations, then atomically updates status.
/// </summary>
internal sealed class ExamHandler(
    IReportsRepository reportsRepository,
    IExamFlowService examFlowService,
    IAuditService auditService,
    ILogger<ExamHandler> logger) : IExamHandler
{
    public async Task<ReviewActionResult> ApproveAsync(long examId, Actor executor, CancellationToken cancellationToken)
    {
        var fetch = await FetchExamAsync(examId, cancellationToken);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var exam = fetch.Value!;

        if (exam.Outcome == ExamOutcome.Passed)
            return new ReviewActionResult(false, "User was auto-admitted — use Dismiss to acknowledge or Deny to override");

        var result = await examFlowService.ApproveExamResultAsync(
            exam.User, exam.Chat, examId, executor, cancellationToken);

        if (!result.Success)
            return new ReviewActionResult(false, result.ErrorMessage ?? "Approval failed");

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, examId, ReportStatus.Reviewed, executor, "approve",
            "Manually approved after exam failure",
            async () =>
            {
                var current = await reportsRepository.GetExamResultAsync(examId, cancellationToken);
                return current != null
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedBy, current.ActionTaken, current.ReviewedAt)
                    : new ReviewActionResult(false, $"Exam result {examId} could not be updated");
            },
            cancellationToken);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Exam review {ExamId}: User {User} approved by {Executor}, permissions restored",
            examId, exam.User.ToLogInfo(), executor.DisplayName);

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, Actor.FromUserIdentity(exam.User),
            $"Approved after exam failure (exam #{examId})", cancellationToken);

        return new ReviewActionResult(true,
            "User approved - permissions restored, teaser deleted",
            ActionName: "Approve");
    }

    public async Task<ReviewActionResult> DenyAsync(long examId, Actor executor, CancellationToken cancellationToken)
    {
        var fetch = await FetchExamAsync(examId, cancellationToken);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var exam = fetch.Value!;

        var result = await examFlowService.DenyExamResultAsync(
            exam.User, exam.Chat, executor, examId, cancellationToken);

        if (!result.Success)
            return new ReviewActionResult(false, result.ErrorMessage ?? "Denial failed");

        var statusResult = await TryStampAsync(exam, examId, executor,
            failAction: "deny", failNotes: "Denied entry after exam review",
            overrideAction: "deny (override auto-approval)", overrideNotes: "User kicked",
            cancellationToken);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Exam review {ExamId}: User {User} denied (kicked) by {Executor}",
            examId, exam.User.ToLogInfo(), executor.DisplayName);

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, Actor.FromUserIdentity(exam.User),
            exam.Outcome == ExamOutcome.Passed
                ? $"Overrode exam auto-approval — denied/kicked (exam #{examId})"
                : $"Denied entry — kicked (exam #{examId})",
            cancellationToken);

        return new ReviewActionResult(true,
            "User denied - kicked from chat, teaser deleted",
            ActionName: "Deny");
    }

    public async Task<ReviewActionResult> DenyAndBanAsync(long examId, Actor executor, CancellationToken cancellationToken)
    {
        var fetch = await FetchExamAsync(examId, cancellationToken);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var exam = fetch.Value!;

        var result = await examFlowService.DenyAndBanExamResultAsync(
            exam.User, exam.Chat, executor, examId, cancellationToken);

        if (!result.Success)
            return new ReviewActionResult(false, result.ErrorMessage ?? "Ban failed");

        var statusResult = await TryStampAsync(exam, examId, executor,
            failAction: "deny_ban", failNotes: "Denied and banned after exam review",
            overrideAction: "deny+ban (override auto-approval)", overrideNotes: "User banned",
            cancellationToken);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Exam review {ExamId}: User {User} denied and banned by {Executor}",
            examId, exam.User.ToLogInfo(), executor.DisplayName);

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, Actor.FromUserIdentity(exam.User),
            exam.Outcome == ExamOutcome.Passed
                ? $"Overrode exam auto-approval — denied & banned (exam #{examId})"
                : $"Denied entry — banned (exam #{examId})",
            cancellationToken);

        return new ReviewActionResult(true,
            "User denied and banned, teaser deleted",
            ActionName: "DenyAndBan");
    }

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

    /// <summary>
    /// Stamps the review outcome after a winning moderation call. A pass takes the atomic
    /// override path (first admin click wins); a failure takes the existing status-update path.
    /// Returns null when the stamp won (proceed to audit + success); otherwise the failure result.
    /// </summary>
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
}
