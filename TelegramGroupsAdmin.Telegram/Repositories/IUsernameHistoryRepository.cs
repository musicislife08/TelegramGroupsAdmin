using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface IUsernameHistoryRepository
{
    /// <summary>
    /// Record the previous profile values when a change is detected.
    /// </summary>
    Task InsertAsync(long userId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all history entries for a user, most recent first.
    /// </summary>
    Task<List<UsernameHistoryRecord>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);
}
