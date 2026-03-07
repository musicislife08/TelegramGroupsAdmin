using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Service for sending notifications to users through configured channels (Telegram DM, Email, Web Push).
/// Typed intent-based methods accept identity objects and raw domain values.
/// The service owns all formatting, subject lines, and channel-specific rendering.
/// </summary>
public interface INotificationService
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
        long? reporterUserId,
        string? reporterName,
        bool isAutomated,
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
        long examFailureId,
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
        CancellationToken ct = default);

}
