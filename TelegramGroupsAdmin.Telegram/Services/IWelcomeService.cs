using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

public interface IWelcomeService
{
    Task HandleChatMemberUpdateAsync(ChatMemberUpdated chatMemberUpdate, CancellationToken cancellationToken);
    Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken);
}
