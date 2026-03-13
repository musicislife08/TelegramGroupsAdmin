using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

/// <summary>
/// Unified service for all report action types. Both UI skins (Telegram DM callbacks, web UI)
/// delegate here. Owns per-report-ID semaphore locking to prevent concurrent double-actions.
/// </summary>
public interface IReportActionsService
{
    // Content report actions
    Task<ReviewActionResult> HandleContentSpamAsync(long reportId, Actor executor, CancellationToken ct = default);
    Task<ReviewActionResult> HandleContentBanAsync(long reportId, Actor executor, CancellationToken ct = default);
    Task<ReviewActionResult> HandleContentWarnAsync(long reportId, Actor executor, CancellationToken ct = default);
    Task<ReviewActionResult> HandleContentDismissAsync(long reportId, Actor executor, string? reason = null, CancellationToken ct = default);

    // Profile scan actions
    Task<ReviewActionResult> HandleProfileScanBanAsync(long alertId, Actor executor, CancellationToken ct = default);
    Task<ReviewActionResult> HandleProfileScanKickAsync(long alertId, Actor executor, CancellationToken ct = default);
    Task<ReviewActionResult> HandleProfileScanAllowAsync(long alertId, Actor executor, CancellationToken ct = default);

    // Impersonation actions
    Task<ReviewActionResult> HandleImpersonationConfirmAsync(long alertId, Actor executor, CancellationToken ct = default);
    Task<ReviewActionResult> HandleImpersonationDismissAsync(long alertId, Actor executor, CancellationToken ct = default);
    Task<ReviewActionResult> HandleImpersonationTrustAsync(long alertId, Actor executor, CancellationToken ct = default);

    // Exam actions
    Task<ReviewActionResult> HandleExamApproveAsync(long examId, Actor executor, CancellationToken ct = default);
    Task<ReviewActionResult> HandleExamDenyAsync(long examId, Actor executor, CancellationToken ct = default);
    Task<ReviewActionResult> HandleExamDenyAndBanAsync(long examId, Actor executor, CancellationToken ct = default);
}
