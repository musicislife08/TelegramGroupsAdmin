using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Core Telegram bot service - provides real-time events, cached state, and config change notifications.
/// Injected by UI components and services that need bot capabilities without the BackgroundService lifecycle.
/// </summary>
public interface ITelegramBotService
{
    // Events (aggregated from child services for UI consumption)

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
    event Action<long, MediaType>? OnMediaUpdated;

    /// <summary>
    /// Event raised when chat health status changes.
    /// </summary>
    event Action<ChatHealthStatus>? OnHealthUpdate;

    /// <summary>
    /// Event raised when config change is requested. The polling host subscribes to this.
    /// </summary>
    event Action? ConfigChangeRequested;

    // State

    /// <summary>
    /// Cached bot user info from GetMe() (available after service starts).
    /// Returns null if bot hasn't started yet.
    /// </summary>
    User? BotUserInfo { get; }

    // Actions

    /// <summary>
    /// Request a bot configuration refresh. Raises ConfigChangeRequested event.
    /// Used for dynamic bot enable/disable without requiring application restart.
    /// </summary>
    void NotifyConfigChange();

    /// <summary>
    /// Set the cached bot user info. Called by polling host after GetMe().
    /// </summary>
    void SetBotUserInfo(User? user);
}
