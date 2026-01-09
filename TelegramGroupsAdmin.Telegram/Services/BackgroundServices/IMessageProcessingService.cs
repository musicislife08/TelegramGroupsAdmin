using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

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

    /// <summary>
    /// Event fired when a new message is processed and stored.
    /// </summary>
    event Action<MessageRecord>? OnNewMessage;

    /// <summary>
    /// Event fired when a message edit is processed and stored.
    /// </summary>
    event Action<MessageEditRecord>? OnMessageEdited;

    /// <summary>
    /// Event fired when media is updated for a message.
    /// </summary>
    event Action<long, MediaType>? OnMediaUpdated;

    /// <summary>
    /// Raises the OnMediaUpdated event (called by MediaRefetchWorkerService).
    /// </summary>
    void RaiseMediaUpdated(long messageId, MediaType mediaType);
}
