using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

public interface IImpersonationAlertsRepository
{
    /// <summary>
    /// Creates a new impersonation alert
    /// </summary>
    Task<int> CreateAlertAsync(ImpersonationAlertRecord alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending (unreviewed) alerts, optionally filtered by chat
    /// Ordered by risk level (critical first) then detected date (newest first)
    /// </summary>
    Task<List<ImpersonationAlertRecord>> GetPendingAlertsAsync(long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific alert by ID with all joined data
    /// </summary>
    Task<ImpersonationAlertRecord?> GetAlertAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the verdict for an alert after manual review
    /// </summary>
    Task UpdateVerdictAsync(
        int id,
        ImpersonationVerdict verdict,
        string reviewedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user has any pending alerts
    /// </summary>
    Task<bool> HasPendingAlertAsync(long suspectedUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all alerts for a specific user (suspected user ID)
    /// </summary>
    Task<List<ImpersonationAlertRecord>> GetAlertHistoryAsync(long userId, CancellationToken cancellationToken = default);
}
