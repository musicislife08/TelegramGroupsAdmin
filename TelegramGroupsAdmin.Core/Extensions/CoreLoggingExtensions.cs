using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Extensions;

/// <summary>
/// Extension methods for enriching log messages with human-readable names for Core domain types.
/// </summary>
public static class CoreLoggingExtensions
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Web User Extensions (UserRecord)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(UserRecord? user)
    {
        /// <summary>
        /// Format web user for INFO logs (email only, no ID).
        /// </summary>
        /// <returns>"user@example.com" or "User {id}" if email unavailable</returns>
        public string ToLogInfo()
            => LogDisplayName.WebUserInfo(user?.Email, user?.Id);

        /// <summary>
        /// Format web user for DEBUG/WARNING/ERROR logs (email + ID).
        /// </summary>
        /// <returns>"user@example.com (abc-123)" or "Unknown User (abc-123)"</returns>
        public string ToLogDebug()
            => LogDisplayName.WebUserDebug(user?.Email, user?.Id ?? "unknown");

        /// <summary>
        /// Format web user for INFO logs with userId fallback when user is null.
        /// </summary>
        /// <param name="userId">User ID (used as fallback when user is null)</param>
        /// <returns>"user@example.com" or "User {userId}" if email unavailable</returns>
        public string ToLogInfo(string userId)
            => LogDisplayName.WebUserInfo(user?.Email, userId);

        /// <summary>
        /// Format web user for DEBUG/WARNING/ERROR logs with userId fallback when user is null.
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>"user@example.com (userId)" or "Unknown User (userId)"</returns>
        public string ToLogDebug(string userId)
            => LogDisplayName.WebUserDebug(user?.Email, userId);
    }
}
