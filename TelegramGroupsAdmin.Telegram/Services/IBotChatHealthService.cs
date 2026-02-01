using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for chat health monitoring, admin caching, and refresh orchestration.
/// Singleton service that orchestrates bulk refresh operations for health checks and admin caches.
/// Membership handling has moved to IBotChatService (scoped).
/// </summary>
public interface IBotChatHealthService
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
    /// </summary>
    /// <returns>HashSet of chat IDs with "Healthy" status</returns>
    HashSet<long> GetHealthyChatIds();

    /// <summary>
    /// Filters a list of chat IDs to only include healthy chats (bot has admin permissions).
    /// </summary>
    /// <param name="chatIds">Chat IDs to filter</param>
    /// <returns>Only chat IDs that are in the healthy set</returns>
    List<long> FilterHealthyChats(IEnumerable<long> chatIds);

    /// <summary>
    /// Refresh admin cache for all active managed chats on startup.
    /// </summary>
    Task RefreshAllChatAdminsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh admin list for a specific chat (groups/supergroups only).
    /// </summary>
    Task RefreshChatAdminsAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform health check on a specific chat and update cache.
    /// </summary>
    Task RefreshHealthForChatAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh health for all active managed chats (excludes chats where bot was removed).
    /// Also backfills missing chat icons to ensure they're fetched eventually.
    /// </summary>
    Task RefreshAllHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh a single chat (for manual UI refresh button).
    /// Includes admin list, health check, and optionally chat icon.
    /// </summary>
    Task RefreshSingleChatAsync(long chatId, bool includeIcon = true, CancellationToken cancellationToken = default);
}
