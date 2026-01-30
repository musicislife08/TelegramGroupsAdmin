using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram user operations.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for user operations.
/// Services should use IBotUserService which orchestrates this handler.
/// </summary>
public interface IBotUserHandler
{
    /// <summary>Get basic information about the bot.</summary>
    Task<User> GetMeAsync(CancellationToken ct = default);

    /// <summary>Get information about a member of a chat.</summary>
    Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default);
}
