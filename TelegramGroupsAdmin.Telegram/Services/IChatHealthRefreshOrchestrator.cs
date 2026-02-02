namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Orchestrates chat health refresh operations.
/// Singleton service that performs health checks and updates IChatHealthCache.
/// For reading cached health state, inject IChatHealthCache directly.
/// For event subscriptions, inject IChatHealthCache.OnHealthUpdate.
/// </summary>
public interface IChatHealthRefreshOrchestrator
{
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
