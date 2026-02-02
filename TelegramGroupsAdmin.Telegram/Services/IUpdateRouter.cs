using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Interface for routing Telegram updates to appropriate services.
/// Enables unit testing of routing logic independently of polling mechanism.
/// </summary>
public interface IUpdateRouter
{
    /// <summary>
    /// Route a Telegram update to the appropriate service based on update type.
    /// </summary>
    /// <param name="update">The Telegram update to route</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <remarks>
    /// Creates a scope per-update and routes to:
    /// - MyChatMember → IBotChatService (bot added/removed from chats)
    /// - ChatMember → IBotChatService + IWelcomeService (user joins/leaves/admin changes)
    /// - CallbackQuery → IBanCallbackService, IReportCallbackService, IWelcomeService (inline button clicks)
    /// - Message → IMessageProcessingService (new messages)
    /// - EditedMessage → IMessageProcessingService (message edits)
    /// </remarks>
    Task RouteUpdateAsync(Update update, CancellationToken cancellationToken = default);
}
