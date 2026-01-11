using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Handles callback queries for report moderation action buttons in DMs.
/// </summary>
public interface IReportCallbackHandler
{
    /// <summary>
    /// Returns true if this handler can handle the given callback data.
    /// </summary>
    bool CanHandle(string callbackData);

    /// <summary>
    /// Handle a report action callback query.
    /// </summary>
    Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken = default);
}
