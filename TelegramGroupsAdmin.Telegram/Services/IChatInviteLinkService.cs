using Telegram.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for retrieving chat invite links from Telegram API
/// Caches links in database (configs.invite_link) to avoid repeated API calls
/// </summary>
public interface IChatInviteLinkService
{
    /// <summary>
    /// Get invite link for chat (from cache or Telegram API)
    /// Returns null if bot lacks permissions or chat is private without link
    /// </summary>
    Task<string?> GetInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken ct = default);

    /// <summary>
    /// Refresh invite link from Telegram API and update cache
    /// Use this to validate cached link is still valid (e.g., in health checks)
    /// </summary>
    Task<string?> RefreshInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken ct = default);
}
