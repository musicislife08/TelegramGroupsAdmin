using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// Extension methods for Telegram.Bot.Types.User logging formatting.
/// Uses C# 13 extension syntax for clean user.ToLogInfo() calls.
/// </summary>
public static class TelegramBotUserExtensions
{
    extension(User? user)
    {
        /// <summary>
        /// Format Telegram Bot API user for INFO-level logs (name only).
        /// </summary>
        /// <returns>"John Doe" or "User {id}" if name unavailable</returns>
        public string ToLogInfo()
            => LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, user?.Id ?? 0);

        /// <summary>
        /// Format Telegram Bot API user for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        /// <returns>"John Doe (123)" or "User {id} ({id})" if name unavailable</returns>
        public string ToLogDebug()
            => LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, user?.Id ?? 0);
    }
}
