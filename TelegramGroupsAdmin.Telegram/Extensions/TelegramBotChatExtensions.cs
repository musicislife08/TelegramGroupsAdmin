using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// Extension methods for Telegram.Bot.Types.Chat logging formatting.
/// Uses C# 13 extension syntax for clean chat.ToLogInfo() calls.
/// </summary>
public static class TelegramBotChatExtensions
{
    extension(Chat? chat)
    {
        /// <summary>
        /// Format Telegram Bot API chat for INFO-level logs (name only).
        /// </summary>
        /// <returns>"Chat Name" or "Chat {id}" if name unavailable</returns>
        public string ToLogInfo()
            => LogDisplayName.ChatInfo(chat?.Title ?? chat?.Username, chat?.Id ?? 0);

        /// <summary>
        /// Format Telegram Bot API chat for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        /// <returns>"Chat Name (-123)" or "Chat {id} ({id})" if name unavailable</returns>
        public string ToLogDebug()
            => LogDisplayName.ChatDebug(chat?.Title ?? chat?.Username, chat?.Id ?? 0);
    }
}
