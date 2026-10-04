using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface IUsernameHistoryRepository
{
    // Renames are recorded only by ITelegramUserRepository.GetOrUpdateAsync, inside the
    // transaction that changes the names.

    /// <summary>
    /// Get all history entries for a user, most recent first.
    /// </summary>
    Task<List<UsernameHistoryRecord>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a rename of the user was recorded strictly after <paramref name="since"/> (typically
    /// the user's last profile scan). A null <paramref name="since"/> (never scanned) counts any
    /// recorded rename.
    /// </summary>
    Task<bool> HasChangeSinceAsync(long userId, DateTimeOffset? since, CancellationToken cancellationToken = default);
}
