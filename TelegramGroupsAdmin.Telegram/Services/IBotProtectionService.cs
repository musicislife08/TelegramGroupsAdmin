using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for protecting chats against unauthorized bots
/// Phase 6.1: Bot Auto-Ban
/// </summary>
public interface IBotProtectionService
{
    /// <summary>
    /// Check if a user is a bot and should be banned based on configuration
    /// Returns true if the bot should be allowed (whitelisted or admin-invited)
    /// Returns false if the bot should be banned
    /// </summary>
    Task<bool> ShouldAllowBotAsync(long chatId, User user, ChatMemberUpdated? chatMemberUpdate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ban a bot from the chat and log the event
    /// </summary>
    Task BanBotAsync(ITelegramBotClient botClient, long chatId, User bot, string reason, CancellationToken cancellationToken = default);
}
