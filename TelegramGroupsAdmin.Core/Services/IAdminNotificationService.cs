using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Admin-facing notifications: routed to web users with chat access and unlinked Telegram
/// chat admins, filtered by each web user's per-event channel preferences (DM, email, web push).
/// User-facing, opt-in notifications live on IUserNotificationService.
/// </summary>
public interface IAdminNotificationService
{
    // ── Chat-contextual (audience = chat admins + global admins + owners, deduplicated) ──

    Task<Dictionary<string, bool>> SendSpamBanNotificationAsync(
        ChatIdentity chat,
        UserIdentity user,
        Actor? bannedBy,
        double netScore,
        double score,
        string? detectionReason,
        int chatsAffected,
        bool messageDeleted,
        int messageId,
        string? messagePreview,
        string? photoPath,
        string? videoPath,
        CancellationToken ct = default);

    Task<Dictionary<string, bool>> SendReportNotificationAsync(
        ChatIdentity chat,
        UserIdentity reportedUser,
        Actor reporter,
        string messagePreview,
        string? photoPath,
        long reportId,
        ReportType reportType,
        CancellationToken ct = default);

    Task<Dictionary<string, bool>> SendProfileScanAlertAsync(
        ChatIdentity chat,
        UserIdentity user,
        decimal score,
        string signals,
        string? aiReason,
        long reportId,
        CancellationToken ct = default);

    Task<Dictionary<string, bool>> SendExamFailureNotificationAsync(
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

    Task<Dictionary<string, bool>> SendBanNotificationAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        ChatIdentity? chat = null,
        CancellationToken ct = default);

    Task<Dictionary<string, bool>> SendMalwareDetectedAsync(
        ChatIdentity chat,
        UserIdentity user,
        string malwareDetails,
        CancellationToken ct = default);

    Task<Dictionary<string, bool>> SendAdminChangedAsync(
        ChatIdentity chat,
        UserIdentity user,
        bool promoted,
        bool isCreator,
        CancellationToken ct = default);

    // ── Infrastructure (audience = owners only) ──

    Task<Dictionary<string, bool>> SendBackupFailedAsync(
        string tableName,
        string error,
        CancellationToken ct = default);

    Task<Dictionary<string, bool>> SendChatHealthWarningAsync(
        string chatName,
        string status,
        bool isAdmin,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken = default);

}
