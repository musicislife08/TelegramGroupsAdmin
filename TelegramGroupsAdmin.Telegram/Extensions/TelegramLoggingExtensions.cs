using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// Extension methods for enriching log messages with human-readable names.
/// </summary>
public static class TelegramLoggingExtensions
{
    // ═══════════════════════════════════════════════════════════════════════════
    // SDK Type Extensions (Telegram.Bot User - no DB fetch needed)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(User? user)
    {
        /// <summary>
        /// Format SDK User for INFO logs (name only, no ID).
        /// </summary>
        public string ToLogInfo()
            => LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, user?.Id ?? 0);

        /// <summary>
        /// Format SDK User for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        public string ToLogDebug()
            => LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, user?.Id ?? 0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SDK Type Extensions (Telegram.Bot Chat - no DB fetch needed)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(Chat? chat)
    {
        /// <summary>
        /// Format SDK Chat for INFO logs (name only, no ID).
        /// </summary>
        public string ToLogInfo()
            => LogDisplayName.ChatInfo(chat?.Title, chat?.Id ?? 0);

        /// <summary>
        /// Format SDK Chat for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        public string ToLogDebug()
            => LogDisplayName.ChatDebug(chat?.Title, chat?.Id ?? 0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DB Entity Extensions (TelegramUserDto)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(TelegramUserDto? user)
    {
        /// <summary>
        /// Format DB user entity for INFO logs (name only, no ID).
        /// </summary>
        public string ToLogInfo()
            => LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, user?.TelegramUserId ?? 0);

        /// <summary>
        /// Format DB user entity for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        public string ToLogDebug()
            => LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, user?.TelegramUserId ?? 0);

        /// <summary>
        /// Format DB user entity for INFO logs with userId fallback when user is null.
        /// </summary>
        /// <param name="userId">Telegram user ID (used as fallback when user is null)</param>
        public string ToLogInfo(long userId)
            => LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, userId);

        /// <summary>
        /// Format DB user entity for DEBUG/WARNING/ERROR logs with userId fallback when user is null.
        /// </summary>
        /// <param name="userId">Telegram user ID</param>
        public string ToLogDebug(long userId)
            => LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, userId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // UI Model Extensions (TelegramUser record)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(TelegramUser? user)
    {
        /// <summary>
        /// Format UI user model for INFO logs (name only, no ID).
        /// </summary>
        public string ToLogInfo()
            => LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, user?.TelegramUserId ?? 0);

        /// <summary>
        /// Format UI user model for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        public string ToLogDebug()
            => LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, user?.TelegramUserId ?? 0);

        /// <summary>
        /// Format UI user model for INFO logs with userId fallback when user is null.
        /// </summary>
        /// <param name="userId">Telegram user ID (used as fallback when user is null)</param>
        public string ToLogInfo(long userId)
            => LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, userId);

        /// <summary>
        /// Format UI user model for DEBUG/WARNING/ERROR logs with userId fallback when user is null.
        /// </summary>
        /// <param name="userId">Telegram user ID</param>
        public string ToLogDebug(long userId)
            => LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, userId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // UI Model Extensions (ManagedChatRecord)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(ManagedChatRecord? chat)
    {
        /// <summary>
        /// Format managed chat record for INFO logs (name only, no ID).
        /// </summary>
        public string ToLogInfo()
            => LogDisplayName.ChatInfo(chat?.ChatName, chat?.ChatId ?? 0);

        /// <summary>
        /// Format managed chat record for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        public string ToLogDebug()
            => LogDisplayName.ChatDebug(chat?.ChatName, chat?.ChatId ?? 0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Repository Async Extensions (for cases where only ID is available)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(ITelegramUserRepository repo)
    {
        /// <summary>
        /// Get user display name for logging. Looks up user from database.
        /// </summary>
        /// <param name="userId">Telegram user ID to look up</param>
        /// <param name="includeId">If true, uses Debug format (name + ID). If false, uses Info format (name only).</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Formatted display name for logging</returns>
        public async Task<string> GetUserLogDisplayAsync(
            long userId,
            bool includeId,
            CancellationToken ct = default)
        {
            var user = await repo.GetByTelegramIdAsync(userId, ct);
            return includeId
                ? LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, userId)
                : LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, userId);
        }
    }

    extension(IManagedChatsRepository repo)
    {
        /// <summary>
        /// Get chat display name for logging. Looks up chat from database.
        /// </summary>
        /// <param name="chatId">Telegram chat ID to look up</param>
        /// <param name="includeId">If true, uses Debug format (name + ID). If false, uses Info format (name only).</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Formatted display name for logging</returns>
        public async Task<string> GetChatLogDisplayAsync(
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
}
