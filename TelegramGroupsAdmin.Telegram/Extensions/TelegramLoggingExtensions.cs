using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

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
    // UI Model Extensions (MessageRecord)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(MessageRecord? message)
    {
        /// <summary>
        /// Format message record for INFO logs (message ID + chat name).
        /// </summary>
        public string ToLogInfo()
            => message == null
                ? "null"
                : $"Message {message.MessageId} in {message.Chat.ChatName ?? $"chat {message.Chat.Id}"}";

        /// <summary>
        /// Format message record for DEBUG/WARNING/ERROR logs (message ID + chat ID + user).
        /// </summary>
        public string ToLogDebug()
            => message == null
                ? "null"
                : $"Message {message.MessageId} in {message.Chat.ChatName ?? "unknown"} ({message.Chat.Id}) from {message.User.DisplayName}";
    }

}
