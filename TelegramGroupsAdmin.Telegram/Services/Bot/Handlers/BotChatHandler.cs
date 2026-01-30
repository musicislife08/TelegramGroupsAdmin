using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram chat operations.
/// Thin wrapper around ITelegramBotClient - no business logic, just API calls.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for chat operations.
/// </summary>
public class BotChatHandler(ITelegramBotClientFactory botClientFactory) : IBotChatHandler
{
    private readonly ITelegramBotClientFactory _botClientFactory = botClientFactory;

    public async Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.GetChat(chatId, ct);
    }

    public async Task<ChatMember[]> GetChatAdministratorsAsync(long chatId, CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.GetChatAdministrators(chatId, ct);
    }

    public async Task<string> ExportChatInviteLinkAsync(long chatId, CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.ExportChatInviteLink(chatId, ct);
    }

    public async Task LeaveChatAsync(long chatId, CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        await client.LeaveChat(chatId, ct);
    }
}
