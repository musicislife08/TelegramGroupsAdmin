using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Unified repository for all report types (ContentReport, ImpersonationAlert, ExamFailure).
/// </summary>
public interface IReportsRepository
{
    // ============================================================
    // Generic report operations (work for all types)
    // ============================================================

    /// <summary>
    /// Get a report by ID (any type)
    /// </summary>
    Task<ReportBase?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending reports, optionally filtered by chat and/or type.
    /// Ordered by creation date (newest first).
    /// </summary>
    Task<List<ReportBase>> GetPendingAsync(
        long? chatId = null,
        ReportType? type = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get reports with optional filters.
    /// </summary>
    Task<List<ReportBase>> GetAsync(
        long? chatId = null,
        ReportType? type = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of pending reports, optionally filtered by chat and/or type.
    /// </summary>
    Task<int> GetPendingCountAsync(
        long? chatId = null,
        ReportType? type = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update report status and reviewer info.
    /// </summary>
    Task UpdateStatusAsync(
        long reportId,
        ReportStatus status,
        string reviewedBy,
        string actionTaken,
        string? adminNotes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically updates report status only if still pending.
    /// Returns false if report was already handled (race condition).
    /// </summary>
    Task<bool> TryUpdateStatusAsync(
        long reportId,
        ReportStatus newStatus,
        string reviewedBy,
        string actionTaken,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old resolved reports (cleanup). Returns count deleted.
    /// </summary>
    Task<int> DeleteOldReportsAsync(
        DateTimeOffset olderThanTimestamp,
        ReportType? type = null,
        CancellationToken cancellationToken = default);

    // ============================================================
    // ContentReport-specific operations (Type = ContentReport)
    // ============================================================

    /// <summary>
    /// Insert a new content report.
    /// </summary>
    Task<long> InsertContentReportAsync(Report report, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get content report by ID with full context.
    /// </summary>
    Task<Report?> GetContentReportAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get content reports with optional filters.
    /// </summary>
    Task<List<Report>> GetContentReportsAsync(
        long? chatId = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending content reports, optionally filtered by chat.
    /// </summary>
    Task<List<Report>> GetPendingContentReportsAsync(
        long? chatId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a message already has a pending content report (duplicate prevention).
    /// </summary>
    Task<Report?> GetExistingPendingContentReportAsync(
        int messageId,
        long chatId,
        CancellationToken cancellationToken = default);

    // ============================================================
    // ImpersonationAlert-specific operations (Type = ImpersonationAlert)
    // ============================================================

    /// <summary>
    /// Insert a new impersonation alert.
    /// </summary>
    Task<long> InsertImpersonationAlertAsync(
        ImpersonationAlertRecord alert,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get impersonation alert by ID with full context.
    /// </summary>
    Task<ImpersonationAlertRecord?> GetImpersonationAlertAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending impersonation alerts, ordered by risk level then date.
    /// </summary>
    Task<List<ImpersonationAlertRecord>> GetPendingImpersonationAlertsAsync(
        long? chatId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user has any pending impersonation alerts.
    /// </summary>
    Task<bool> HasPendingImpersonationAlertAsync(
        long suspectedUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all impersonation alerts for a specific user (suspected user ID).
    /// </summary>
    Task<List<ImpersonationAlertRecord>> GetImpersonationAlertHistoryAsync(
        long userId,
        CancellationToken cancellationToken = default);

    // ============================================================
    // ExamFailure-specific operations (Type = ExamFailure)
    // ============================================================

    /// <summary>
    /// Insert a new exam failure report.
    /// </summary>
    Task<long> InsertExamFailureAsync(
        ExamFailureRecord examFailure,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get exam failure by ID with full context.
    /// </summary>
    Task<ExamFailureRecord?> GetExamFailureAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending exam failures for a specific chat.
    /// </summary>
    Task<List<ExamFailureRecord>> GetPendingExamFailuresAsync(
        long? chatId = null,
        CancellationToken cancellationToken = default);
}
