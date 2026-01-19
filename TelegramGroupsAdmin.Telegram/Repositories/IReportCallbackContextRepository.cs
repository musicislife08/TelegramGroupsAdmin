using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing report callback button contexts.
/// Contexts store report/chat/user data for DM buttons, enabling short callback IDs.
/// </summary>
public interface IReportCallbackContextRepository
{
    /// <summary>
    /// Create a new callback context and return its ID.
    /// </summary>
    Task<long> CreateAsync(
        ReportCallbackContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a callback context by ID.
    /// </summary>
    Task<ReportCallbackContext?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a callback context by ID.
    /// </summary>
    Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all callback contexts for a report (cleanup when report is handled via web UI).
    /// </summary>
    Task DeleteByReportIdAsync(
        long reportId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all expired callback contexts (cleanup job).
    /// </summary>
    Task<int> DeleteExpiredAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default);
}
