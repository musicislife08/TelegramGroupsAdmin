using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// Extension methods for enriching log messages with human-readable names from database lookups.
/// </summary>
public static class LoggingExtensions
{
    // ═══════════════════════════════════════════════════════════════════════════
    // DB Entity Extensions (TelegramUserDto)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Format DB user entity for INFO logs (name only, no ID).
    /// </summary>
    public static string ToLogInfo(this TelegramUserDto? user)
        => LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, user?.TelegramUserId ?? 0);

    /// <summary>
    /// Format DB user entity for DEBUG/WARNING/ERROR logs (name + ID).
    /// </summary>
    public static string ToLogDebug(this TelegramUserDto? user)
        => LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, user?.TelegramUserId ?? 0);

    // ═══════════════════════════════════════════════════════════════════════════
    // Repository Async Extensions (for cases where only ID is available)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get user display name for logging. Looks up user from database.
    /// </summary>
    /// <param name="repo">Telegram user repository</param>
    /// <param name="userId">Telegram user ID to look up</param>
    /// <param name="includeId">If true, uses Debug format (name + ID). If false, uses Info format (name only).</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Formatted display name for logging</returns>
    public static async Task<string> GetUserLogDisplayAsync(
        this ITelegramUserRepository repo,
        long userId,
        bool includeId,
        CancellationToken ct = default)
    {
        var user = await repo.GetByTelegramIdAsync(userId, ct);
        return includeId
            ? LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, userId)
            : LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, userId);
    }

    /// <summary>
    /// Get chat display name for logging. Looks up chat from database.
    /// </summary>
    /// <param name="repo">Managed chats repository</param>
    /// <param name="chatId">Telegram chat ID to look up</param>
    /// <param name="includeId">If true, uses Debug format (name + ID). If false, uses Info format (name only).</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Formatted display name for logging</returns>
    public static async Task<string> GetChatLogDisplayAsync(
        this IManagedChatsRepository repo,
        long chatId,
        bool includeId,
        CancellationToken ct = default)
    {
        var chat = await repo.GetByChatIdAsync(chatId, ct);
        return includeId
            ? LogDisplayName.ChatDebug(chat?.ChatName, chatId)
            : LogDisplayName.ChatInfo(chat?.ChatName, chatId);
    }
}
