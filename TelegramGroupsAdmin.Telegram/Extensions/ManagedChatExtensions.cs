using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// Extension methods for ManagedChatRecord logging formatting.
/// Handles null chats gracefully by falling back to "Chat {id}" format.
/// </summary>
public static class ManagedChatExtensions
{
    extension(ManagedChatRecord? chat)
    {
        /// <summary>
        /// Format chat for INFO-level logs (name only).
        /// </summary>
        /// <param name="chatId">Chat ID (used as fallback when chat is null)</param>
        /// <returns>"Chat Name" or "Chat 123" if name unavailable</returns>
        public string ToLogInfo(long chatId)
            => LogDisplayName.ChatInfo(chat?.ChatName, chatId);

        /// <summary>
        /// Format chat for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        /// <param name="chatId">Chat ID</param>
        /// <returns>"Chat Name (-123)" or "Chat 123 (-123)" if name unavailable</returns>
        public string ToLogDebug(long chatId)
            => LogDisplayName.ChatDebug(chat?.ChatName, chatId);
    }
}
