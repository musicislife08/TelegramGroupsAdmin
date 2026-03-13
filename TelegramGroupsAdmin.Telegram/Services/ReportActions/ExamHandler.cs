using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

/// <summary>
/// Handles exam failure actions (approve, deny, deny-and-ban).
/// Delegates to IExamFlowService for actual exam operations, then atomically updates status.
/// </summary>
internal sealed class ExamHandler(
    IReportsRepository reportsRepository,
    IExamFlowService examFlowService,
    IReportCallbackContextRepository callbackContextRepo,
    ILogger<ExamHandler> logger) : IExamHandler
{
    public async Task<ReviewActionResult> ApproveAsync(long examId, Actor executor, CancellationToken ct)
    {
        var (exam, error) = await FetchExamAsync(examId, ct);
        if (error != null) return error;

        var result = await examFlowService.ApproveExamFailureAsync(
            exam!.User, exam.Chat, examId, executor, ct);

        if (!result.Success)
            return new ReviewActionResult(false, result.ErrorMessage ?? "Approval failed");

        var statusResult = await UpdateStatusAtomicallyAsync(
            examId, ReportStatus.Reviewed, executor, "approve",
            "Manually approved after exam failure", ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Exam review {ExamId}: User {User} approved by {Executor}, permissions restored",
            examId, exam.User.ToLogInfo(), executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(examId, ct);

        return new ReviewActionResult(true,
            "User approved - permissions restored, teaser deleted",
            ActionName: "Approve");
    }

    public async Task<ReviewActionResult> DenyAsync(long examId, Actor executor, CancellationToken ct)
    {
        var (exam, error) = await FetchExamAsync(examId, ct);
        if (error != null) return error;

        var result = await examFlowService.DenyExamFailureAsync(
            exam!.User, exam.Chat, executor, ct);

        if (!result.Success)
            return new ReviewActionResult(false, result.ErrorMessage ?? "Denial failed");

        var statusResult = await UpdateStatusAtomicallyAsync(
            examId, ReportStatus.Reviewed, executor, "deny",
            "Denied entry after exam review", ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Exam review {ExamId}: User {User} denied (kicked) by {Executor}",
            examId, exam.User.ToLogInfo(), executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(examId, ct);

        return new ReviewActionResult(true,
            "User denied - kicked from chat, teaser deleted",
            ActionName: "Deny");
    }

    public async Task<ReviewActionResult> DenyAndBanAsync(long examId, Actor executor, CancellationToken ct)
    {
        var (exam, error) = await FetchExamAsync(examId, ct);
        if (error != null) return error;

        var result = await examFlowService.DenyAndBanExamFailureAsync(
            exam!.User, exam.Chat, executor, ct);

        if (!result.Success)
            return new ReviewActionResult(false, result.ErrorMessage ?? "Ban failed");

        var statusResult = await UpdateStatusAtomicallyAsync(
            examId, ReportStatus.Reviewed, executor, "deny_ban",
            "Denied and banned after exam review", ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Exam review {ExamId}: User {User} denied and banned by {Executor}",
            examId, exam.User.ToLogInfo(), executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(examId, ct);

        return new ReviewActionResult(true,
            "User denied and banned, teaser deleted",
            ActionName: "DenyAndBan");
    }

    private async Task<(ExamFailureRecord? Exam, ReviewActionResult? Error)> FetchExamAsync(
        long examId, CancellationToken ct)
    {
        var exam = await reportsRepository.GetExamFailureAsync(examId, ct);
        if (exam == null)
            return (null, new ReviewActionResult(false, $"Exam failure {examId} not found"));

        if (exam.ReviewedAt.HasValue)
        {
            var handledBy = exam.ReviewedBy ?? "another admin";
            var action = exam.ActionTaken ?? "unknown";
            var time = exam.ReviewedAt.Value.UtcDateTime.ToString("g");
            return (null, new ReviewActionResult(false,
                $"Already handled by {handledBy} ({action}) at {time} UTC"));
        }

        return (exam, null);
    }

    private async Task<ReviewActionResult?> UpdateStatusAtomicallyAsync(
        long examId, ReportStatus status, Actor executor, string actionTaken, string notes, CancellationToken ct)
    {
        var updated = await reportsRepository.TryUpdateStatusAsync(
            examId, status, executor.GetDisplayText(), actionTaken, notes, ct);

        if (updated) return null;

        // Race condition: re-fetch for attribution
        var current = await reportsRepository.GetExamFailureAsync(examId, ct);
        if (current?.ReviewedAt.HasValue == true)
        {
            var handledBy = current.ReviewedBy ?? "another admin";
            var action = current.ActionTaken ?? "unknown";
            var time = current.ReviewedAt.Value.UtcDateTime.ToString("g");
            return new ReviewActionResult(false,
                $"Already handled by {handledBy} ({action}) at {time} UTC");
        }

        return new ReviewActionResult(false, $"Exam failure {examId} could not be updated");
    }
}
