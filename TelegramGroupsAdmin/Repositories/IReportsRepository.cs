using TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for managing user-submitted reports (/report command)
/// </summary>
public interface IReportsRepository
{
    /// <summary>
    /// Insert a new report
    /// </summary>
    Task<long> InsertAsync(Report report);

    /// <summary>
    /// Get a report by ID
    /// </summary>
    Task<Report?> GetByIdAsync(long id);

    /// <summary>
    /// Get all pending reports (optionally filtered by chat)
    /// </summary>
    Task<List<Report>> GetPendingReportsAsync(long? chatId = null);

    /// <summary>
    /// Get all reports with optional filters
    /// </summary>
    Task<List<Report>> GetReportsAsync(
        long? chatId = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0);

    /// <summary>
    /// Update report status and reviewer info
    /// </summary>
    Task UpdateReportStatusAsync(
        long reportId,
        ReportStatus status,
        string reviewedBy,
        string actionTaken,
        string? adminNotes = null);

    /// <summary>
    /// Get count of pending reports
    /// </summary>
    Task<int> GetPendingCountAsync(long? chatId = null);

    /// <summary>
    /// Delete old reports (cleanup)
    /// </summary>
    Task DeleteOldReportsAsync(DateTimeOffset olderThanTimestamp);
}
