using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Interface for processing Telegram updates - enables unit testing of update routing logic.
/// </summary>
/// <remarks>
/// The Telegram.Bot library's ReceiveAsync method takes callbacks that receive ITelegramBotClient.
/// By extracting update processing to this interface, we can test the routing logic independently
/// of the polling mechanism by injecting fake Update objects directly.
/// </remarks>
public interface IUpdateProcessor
{
    /// <summary>
    /// Process a Telegram update - routes to appropriate handler based on update type.
    /// </summary>
    /// <param name="update">The Telegram update to process</param>
    /// <param name="ct">Cancellation token</param>
    /// <remarks>
    /// Routes updates to the appropriate service:
    /// - MyChatMember → ChatManagementService (bot added/removed from chats)
    /// - ChatMember → ChatManagementService + WelcomeService (user joins/leaves/promotions)
    /// - CallbackQuery → WelcomeService (inline button clicks)
    /// - Message → MessageProcessingService (new messages)
    /// - EditedMessage → MessageProcessingService (message edits)
    /// </remarks>
    Task ProcessUpdateAsync(Update update, CancellationToken ct = default);
}
