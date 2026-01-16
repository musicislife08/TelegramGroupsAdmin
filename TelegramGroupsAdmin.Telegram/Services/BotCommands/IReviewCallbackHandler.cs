using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Handles callback queries for review moderation action buttons in DMs.
/// Routes to type-specific handlers based on ReviewType (Report, ImpersonationAlert, ExamFailure).
/// </summary>
public interface IReviewCallbackHandler
{
    /// <summary>
    /// Returns true if this handler can handle the given callback data.
    /// Supports both legacy 'rpt:' and new 'rev:' prefixes.
    /// </summary>
    bool CanHandle(string callbackData);

    /// <summary>
    /// Handle a review action callback query.
    /// Routes to appropriate handler based on ReviewType.
    /// </summary>
    Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken = default);
}
