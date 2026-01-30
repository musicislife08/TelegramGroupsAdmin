using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram user operations.
/// Thin wrapper around ITelegramBotClient - no business logic, just API calls.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for user operations.
/// </summary>
public class BotUserHandler(ITelegramBotClientFactory botClientFactory) : IBotUserHandler
{
    private readonly ITelegramBotClientFactory _botClientFactory = botClientFactory;

    public async Task<User> GetMeAsync(CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.GetMe(ct);
    }

    public async Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.GetChatMember(chatId, userId, ct);
    }
}
