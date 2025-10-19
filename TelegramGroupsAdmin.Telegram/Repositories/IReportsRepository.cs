using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

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
    /// Get count of pending reports
    /// </summary>
    Task<int> GetPendingCountAsync(long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old reports (cleanup)
    /// </summary>
    Task DeleteOldReportsAsync(DateTimeOffset olderThanTimestamp, CancellationToken cancellationToken = default);
}
