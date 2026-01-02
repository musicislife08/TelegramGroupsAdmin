using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Extensions;

/// <summary>
/// Extension methods for UserRecord (web user) logging formatting.
/// Uses C# 13 extension syntax for clean user.ToLogInfo() calls.
/// </summary>
public static class UserRecordExtensions
{
    extension(UserRecord? user)
    {
        /// <summary>
        /// Format web user for INFO-level logs (email only).
        /// </summary>
        /// <param name="userId">User ID (used as fallback when user is null)</param>
        /// <returns>"user@example.com" or "User {id}" if email unavailable</returns>
        public string ToLogInfo(string userId)
            => LogDisplayName.WebUserInfo(user?.Email, userId);

        /// <summary>
        /// Format web user for DEBUG/WARNING/ERROR logs (email + ID).
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>"user@example.com (abc123)" or "User {id} ({id})" if email unavailable</returns>
        public string ToLogDebug(string userId)
            => LogDisplayName.WebUserDebug(user?.Email, userId);
    }
}
