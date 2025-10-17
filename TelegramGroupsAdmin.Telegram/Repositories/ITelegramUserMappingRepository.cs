using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface ITelegramUserMappingRepository
{
    /// <summary>
    /// Get all active mappings for a web app user
    /// </summary>
    Task<IEnumerable<TelegramUserMappingRecord>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get mapping by Telegram ID (returns null if not found or inactive)
    /// </summary>
    Task<TelegramUserMappingRecord?> GetByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get web app user ID for a Telegram user (for permission checking)
    /// Returns null if not linked or inactive
    /// </summary>
    Task<string?> GetUserIdByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new mapping (called by /link command)
    /// </summary>
    Task<long> InsertAsync(TelegramUserMappingRecord mapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft delete a mapping (user unlinks account)
    /// </summary>
    Task<bool> DeactivateAsync(long mappingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a Telegram account is already linked
    /// </summary>
    Task<bool> IsTelegramIdLinkedAsync(long telegramId, CancellationToken cancellationToken = default);
}
