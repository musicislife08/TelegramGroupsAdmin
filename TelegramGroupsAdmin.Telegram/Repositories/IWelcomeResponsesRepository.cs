using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface IWelcomeResponsesRepository
{
    Task<long> InsertAsync(WelcomeResponse response, CancellationToken cancellationToken = default);
    Task<WelcomeResponse?> GetByUserAndChatAsync(long userId, long chatId, CancellationToken cancellationToken = default);
    Task UpdateResponseAsync(long id, WelcomeResponseType responseType, bool dmSent = false, bool dmFallback = false, CancellationToken cancellationToken = default);
    Task SetTimeoutJobIdAsync(long id, Guid? jobId, CancellationToken cancellationToken = default);
    Task<List<WelcomeResponse>> GetByChatIdAsync(long chatId, int limit = 100, CancellationToken cancellationToken = default);
    Task<WelcomeStats> GetStatsAsync(long? chatId = null, DateTimeOffset? since = null, CancellationToken cancellationToken = default);
}
