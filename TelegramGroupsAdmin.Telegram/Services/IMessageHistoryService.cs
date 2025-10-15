using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for accessing message history events and real-time updates.
/// Abstraction over HistoryBotService to avoid injecting the entire background service into UI components.
/// </summary>
public interface IMessageHistoryService
{
    /// <summary>
    /// Event raised when a new message is received and cached.
    /// </summary>
    event Action<MessageRecord>? OnNewMessage;

    /// <summary>
    /// Event raised when a message is edited.
    /// </summary>
    event Action<MessageEditRecord>? OnMessageEdited;
}
