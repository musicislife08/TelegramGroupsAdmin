using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram chat operations.
/// Thin wrapper around ITelegramApiClient - no business logic, just API calls.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for chat operations.
/// </summary>
public class BotChatHandler(ITelegramBotClientFactory botClientFactory) : IBotChatHandler
{
    private readonly ITelegramBotClientFactory _botClientFactory = botClientFactory;

    public async Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken ct = default)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();
        return await apiClient.GetChatAsync(chatId, ct);
    }

    public async Task<ChatMember[]> GetChatAdministratorsAsync(long chatId, CancellationToken ct = default)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();
        return await apiClient.GetChatAdministratorsAsync(chatId, ct);
    }

    public async Task<string> ExportChatInviteLinkAsync(long chatId, CancellationToken ct = default)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();
        return await apiClient.ExportChatInviteLinkAsync(chatId, ct);
    }

    public async Task LeaveChatAsync(long chatId, CancellationToken ct = default)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();
        await apiClient.LeaveChatAsync(chatId, ct);
    }
}
