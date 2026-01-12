using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing user-submitted reports (/report command)
/// </summary>
public interface IReportsRepository
{
    /// <summary>
    /// Insert a new report
    /// </summary>
    Task<long> InsertAsync(Report report, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a report by ID
    /// </summary>
    Task<Report?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all pending reports (optionally filtered by chat)
    /// </summary>
    Task<List<Report>> GetPendingReportsAsync(long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all reports with optional filters
    /// </summary>
    Task<List<Report>> GetReportsAsync(
        long? chatId = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update report status and reviewer info
    /// </summary>
    Task UpdateReportStatusAsync(
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
    Task<bool> TryUpdateReportStatusAsync(
        long reportId,
        ReportStatus newStatus,
        string reviewedBy,
        string actionTaken,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of pending reports
    /// </summary>
    Task<int> GetPendingCountAsync(long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a message already has a pending report (duplicate prevention)
    /// </summary>
    Task<Report?> GetExistingPendingReportAsync(
        int messageId,
        long chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old resolved reports (cleanup). Returns count of deleted reports.
    /// </summary>
    Task<int> DeleteOldReportsAsync(DateTimeOffset olderThanTimestamp, CancellationToken cancellationToken = default);
}
