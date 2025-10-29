using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

public interface IWelcomeService
{
    Task HandleChatMemberUpdateAsync(ITelegramBotClient botClient, ChatMemberUpdated chatMemberUpdate, CancellationToken cancellationToken);
    Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken);
}
