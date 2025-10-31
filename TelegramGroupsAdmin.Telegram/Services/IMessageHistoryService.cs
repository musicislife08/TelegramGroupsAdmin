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

    /// <summary>
    /// Event raised when media is downloaded and ready for display.
    /// </summary>
    event Action<long, TelegramGroupsAdmin.Telegram.Models.MediaType>? OnMediaUpdated;

    /// <summary>
    /// Notify the bot service that configuration has changed and bot state should be refreshed immediately.
    /// Used for dynamic bot enable/disable without requiring application restart.
    /// </summary>
    void NotifyConfigChange();
}
