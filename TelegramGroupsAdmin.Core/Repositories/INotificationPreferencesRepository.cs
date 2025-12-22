using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Repositories;

/// <summary>
/// Repository for user notification preferences
/// Manages the Channel×Event matrix configuration
/// </summary>
public interface INotificationPreferencesRepository
{
    /// <summary>
    /// Get notification preferences for a user
    /// </summary>
    /// <returns>NotificationConfig or null if no preferences exist</returns>
    Task<NotificationConfig?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get or create notification preferences for a user
    /// Creates default (empty channels) if not exists
    /// </summary>
    Task<NotificationConfig> GetOrCreateAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save notification preferences (upsert)
    /// </summary>
    Task SaveAsync(string userId, NotificationConfig config, CancellationToken cancellationToken = default);
}
