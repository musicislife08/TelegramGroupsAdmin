using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Interface for message processing service.
/// Extracted to enable testing of UpdateProcessor routing logic.
/// </summary>
public interface IMessageProcessingService
{
    /// <summary>
    /// Process a new message from Telegram.
    /// </summary>
    Task HandleNewMessageAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process an edited message from Telegram.
    /// </summary>
    Task HandleEditedMessageAsync(Message editedMessage, CancellationToken cancellationToken = default);
}
