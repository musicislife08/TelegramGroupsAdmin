using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Handles callback queries for ban user selection buttons.
/// </summary>
public interface IBanCallbackHandler
{
    /// <summary>
    /// Returns true if this handler can handle the given callback data.
    /// </summary>
    bool CanHandle(string callbackData);

    /// <summary>
    /// Handle a ban-related callback query.
    /// </summary>
    Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken = default);
}
