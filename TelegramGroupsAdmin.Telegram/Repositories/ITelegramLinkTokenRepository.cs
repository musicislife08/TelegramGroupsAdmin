using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface ITelegramLinkTokenRepository
{
    /// <summary>
    /// Create a new link token
    /// </summary>
    Task InsertAsync(TelegramLinkTokenRecord token);

    /// <summary>
    /// Get token details (for validation)
    /// Returns null if token doesn't exist
    /// </summary>
    Task<TelegramLinkTokenRecord?> GetByTokenAsync(string token);

    /// <summary>
    /// Mark token as used
    /// </summary>
    Task MarkAsUsedAsync(string token, long telegramId);

    /// <summary>
    /// Delete expired tokens (cleanup)
    /// </summary>
    Task DeleteExpiredTokensAsync(DateTimeOffset beforeTimestamp);

    /// <summary>
    /// Get active tokens for a user (for UI display)
    /// </summary>
    Task<IEnumerable<TelegramLinkTokenRecord>> GetActiveTokensForUserAsync(string userId);

    /// <summary>
    /// Revoke all unused tokens for a user (when generating new token)
    /// </summary>
    Task RevokeUnusedTokensForUserAsync(string userId);
}
