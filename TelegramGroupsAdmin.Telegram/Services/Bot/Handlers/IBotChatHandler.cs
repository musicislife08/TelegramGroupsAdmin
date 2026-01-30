using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram chat operations.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for chat operations.
/// Services should use IBotChatService which orchestrates this handler.
/// </summary>
public interface IBotChatHandler
{
    /// <summary>Get information about a chat.</summary>
    Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken ct = default);

    /// <summary>Get a list of administrators in a chat.</summary>
    Task<ChatMember[]> GetChatAdministratorsAsync(long chatId, CancellationToken ct = default);

    /// <summary>Export the invite link for a chat.</summary>
    Task<string> ExportChatInviteLinkAsync(long chatId, CancellationToken ct = default);

    /// <summary>Leave a chat (group, supergroup, or channel).</summary>
    Task LeaveChatAsync(long chatId, CancellationToken ct = default);
}
