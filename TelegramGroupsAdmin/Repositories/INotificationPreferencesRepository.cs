using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Repositories;

public interface INotificationPreferencesRepository
{
    /// <summary>
    /// Get notification preferences for a user (creates default if not exists)
    /// </summary>
    Task<NotificationPreferences> GetOrCreatePreferencesAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Update notification preferences
    /// </summary>
    Task UpdatePreferencesAsync(NotificationPreferences preferences, CancellationToken ct = default);

    /// <summary>
    /// Store an encrypted secret for a notification channel
    /// </summary>
    Task SetProtectedSecretAsync(string userId, string secretKey, string secretValue, CancellationToken ct = default);

    /// <summary>
    /// Retrieve and decrypt a secret for a notification channel
    /// </summary>
    Task<string?> GetProtectedSecretAsync(string userId, string secretKey, CancellationToken ct = default);
}
