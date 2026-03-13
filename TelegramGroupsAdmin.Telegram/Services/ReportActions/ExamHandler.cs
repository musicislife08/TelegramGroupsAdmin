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
    public async Task<ReviewActionResult> ApproveAsync(long examId, Actor executor, CancellationToken cancellationToken)
    {
        var fetch = await FetchExamAsync(examId, cancellationToken);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var exam = fetch.Value!;

        var result = await examFlowService.ApproveExamFailureAsync(
            exam.User, exam.Chat, examId, executor, cancellationToken);

        if (!result.Success)
            return new ReviewActionResult(false, result.ErrorMessage ?? "Approval failed");

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, examId, ReportStatus.Reviewed, executor, "approve",
            "Manually approved after exam failure",
            async () =>
            {
                var current = await reportsRepository.GetExamFailureAsync(examId, cancellationToken);
                return current != null
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedBy, current.ActionTaken, current.ReviewedAt)
                    : new ReviewActionResult(false, $"Exam failure {examId} could not be updated");
            },
            cancellationToken);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Exam review {ExamId}: User {User} approved by {Executor}, permissions restored",
            examId, exam.User.ToLogInfo(), executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(examId, cancellationToken);

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

        var result = await examFlowService.DenyExamFailureAsync(
            exam.User, exam.Chat, executor, cancellationToken);

        if (!result.Success)
            return new ReviewActionResult(false, result.ErrorMessage ?? "Denial failed");

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, examId, ReportStatus.Reviewed, executor, "deny",
            "Denied entry after exam review",
            async () =>
            {
                var current = await reportsRepository.GetExamFailureAsync(examId, cancellationToken);
                return current != null
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedBy, current.ActionTaken, current.ReviewedAt)
                    : new ReviewActionResult(false, $"Exam failure {examId} could not be updated");
            },
            cancellationToken);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Exam review {ExamId}: User {User} denied (kicked) by {Executor}",
            examId, exam.User.ToLogInfo(), executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(examId, cancellationToken);

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

        var result = await examFlowService.DenyAndBanExamFailureAsync(
            exam.User, exam.Chat, executor, cancellationToken);

        if (!result.Success)
            return new ReviewActionResult(false, result.ErrorMessage ?? "Ban failed");

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, examId, ReportStatus.Reviewed, executor, "deny_ban",
            "Denied and banned after exam review",
            async () =>
            {
                var current = await reportsRepository.GetExamFailureAsync(examId, cancellationToken);
                return current != null
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedBy, current.ActionTaken, current.ReviewedAt)
                    : new ReviewActionResult(false, $"Exam failure {examId} could not be updated");
            },
            cancellationToken);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Exam review {ExamId}: User {User} denied and banned by {Executor}",
            examId, exam.User.ToLogInfo(), executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(examId, cancellationToken);

        return new ReviewActionResult(true,
            "User denied and banned, teaser deleted",
            ActionName: "DenyAndBan");
    }

    private async Task<FetchResult<ExamFailureRecord>> FetchExamAsync(long examId, CancellationToken cancellationToken)
    {
        var exam = await reportsRepository.GetExamFailureAsync(examId, cancellationToken);
        if (exam == null)
            return FetchResult<ExamFailureRecord>.Fail($"Exam failure {examId} not found");

        var alreadyHandled = ReportStatusHelper.CheckAlreadyHandled(
            exam.ReviewedBy, exam.ActionTaken, exam.ReviewedAt);
        if (alreadyHandled != null)
            return FetchResult<ExamFailureRecord>.Handled(alreadyHandled.Message);

        return FetchResult<ExamFailureRecord>.Ok(exam);
    }
}
