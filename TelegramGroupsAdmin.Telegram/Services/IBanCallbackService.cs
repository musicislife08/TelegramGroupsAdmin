using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Application-level service for handling ban callback queries from inline buttons.
/// Orchestrates the ban selection workflow, calling bot services for Telegram operations.
/// </summary>
public interface IBanCallbackService
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
