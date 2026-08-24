using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface IWelcomeResponsesRepository
{
    Task<long> InsertAsync(WelcomeResponse response, CancellationToken cancellationToken = default);
    Task<WelcomeResponse?> GetByUserAndChatAsync(long userId, long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the most recent welcome response per chat for a user, across every chat.
    /// Used by ban cleanup, which is global and has no single chat to scope to.
    /// </summary>
    Task<List<WelcomeResponse>> GetByUserAsync(long userId, CancellationToken cancellationToken = default);
    Task UpdateResponseAsync(long id, WelcomeResponseType responseType, bool dmSent = false, bool dmFallback = false, CancellationToken cancellationToken = default);
    Task SetTimeoutJobIdAsync(long id, string? jobId, CancellationToken cancellationToken = default);
    Task<List<WelcomeResponse>> GetByChatIdAsync(long chatId, int limit = 100, CancellationToken cancellationToken = default);
    Task<WelcomeStats> GetStatsAsync(long? chatId = null, DateTimeOffset? since = null, CancellationToken cancellationToken = default);
}
