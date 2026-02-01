using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Pure state storage for chat health data.
/// Singleton service that maintains an in-memory cache of chat health statuses.
/// No Telegram API calls - just stores and retrieves state.
/// Used by IBotChatService and IChatHealthRefreshOrchestrator.
/// </summary>
public interface IChatHealthCache
{
    /// <summary>
    /// Event for real-time UI updates when chat health changes.
    /// </summary>
    event Action<ChatHealthStatus>? OnHealthUpdate;

    /// <summary>
    /// Get cached health status for a chat (null if not yet checked).
    /// </summary>
    ChatHealthStatus? GetCachedHealth(long chatId);

    /// <summary>
    /// Get set of chat IDs where bot has healthy status (admin + required permissions).
    /// Uses cached health data from most recent health check.
    /// Chats with Warning/Error/Unknown status are excluded to prevent action failures.
    /// Returns HashSet for O(1) lookup performance when filtering large chat lists.
    ///
    /// Fail-Closed Behavior: If health cache is empty (cold start before first health check),
    /// returns empty set, causing all moderation actions to be skipped. This prevents
    /// permission errors until health status is confirmed.
    /// </summary>
    /// <returns>HashSet of chat IDs with "Healthy" status</returns>
    HashSet<long> GetHealthyChatIds();

    /// <summary>
    /// Filters a list of chat IDs to only include healthy chats (bot has admin permissions).
    /// DRY helper to avoid repeating health gate pattern across multiple call sites.
    /// </summary>
    /// <param name="chatIds">Chat IDs to filter</param>
    /// <returns>Only chat IDs that are in the healthy set</returns>
    List<long> FilterHealthyChats(IEnumerable<long> chatIds);

    /// <summary>
    /// Update health status for a chat.
    /// Raises OnHealthUpdate event for real-time UI updates.
    /// </summary>
    void SetHealth(long chatId, ChatHealthStatus status);

    /// <summary>
    /// Remove health status for a chat (when chat is removed from management).
    /// </summary>
    void RemoveHealth(long chatId);

    /// <summary>
    /// Get all cached health statuses (for UI display).
    /// </summary>
    IReadOnlyDictionary<long, ChatHealthStatus> GetAllCachedHealth();
}
