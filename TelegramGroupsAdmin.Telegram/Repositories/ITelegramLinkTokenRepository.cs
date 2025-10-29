using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface ITelegramLinkTokenRepository
{
    /// <summary>
    /// Create a new link token
    /// </summary>
    Task InsertAsync(TelegramLinkTokenRecord token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get token details (for validation)
    /// Returns null if token doesn't exist
    /// </summary>
    Task<TelegramLinkTokenRecord?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark token as used
    /// </summary>
    Task MarkAsUsedAsync(string token, long telegramId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete expired tokens (cleanup)
    /// </summary>
    Task DeleteExpiredTokensAsync(DateTimeOffset beforeTimestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active tokens for a user (for UI display)
    /// </summary>
    Task<IEnumerable<TelegramLinkTokenRecord>> GetActiveTokensForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke all unused tokens for a user (when generating new token)
    /// </summary>
    Task RevokeUnusedTokensForUserAsync(string userId, CancellationToken cancellationToken = default);
}
