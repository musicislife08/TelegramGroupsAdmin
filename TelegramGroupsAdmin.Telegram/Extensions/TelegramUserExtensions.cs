using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// Extension methods for TelegramUser logging formatting.
/// Handles null users gracefully by falling back to "User {id}" format.
/// </summary>
public static class TelegramUserExtensions
{
    extension(TelegramUser? user)
    {
        /// <summary>
        /// Format user for INFO-level logs (name only).
        /// </summary>
        /// <param name="userId">Telegram user ID (used as fallback when user is null)</param>
        /// <returns>"John Doe" or "User 123" if name unavailable</returns>
        public string ToLogInfo(long userId)
            => LogDisplayName.UserInfo(user?.FirstName, user?.LastName, user?.Username, userId);

        /// <summary>
        /// Format user for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        /// <param name="userId">Telegram user ID</param>
        /// <returns>"John Doe (123)" or "User 123 (123)" if name unavailable</returns>
        public string ToLogDebug(long userId)
            => LogDisplayName.UserDebug(user?.FirstName, user?.LastName, user?.Username, userId);
    }
}
