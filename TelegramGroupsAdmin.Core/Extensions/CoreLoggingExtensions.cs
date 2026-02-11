using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Extensions;

/// <summary>
/// Extension methods for enriching log messages with human-readable names for Core domain types.
/// </summary>
public static class CoreLoggingExtensions
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Identity Type Extensions (UserIdentity / ChatIdentity)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(UserIdentity? identity)
    {
        /// <summary>
        /// Format user identity for INFO logs (name only, no ID).
        /// </summary>
        public string ToLogInfo()
            => LogDisplayName.UserInfo(identity?.FirstName, identity?.LastName, identity?.Username, identity?.Id ?? 0);

        /// <summary>
        /// Format user identity for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        public string ToLogDebug()
            => LogDisplayName.UserDebug(identity?.FirstName, identity?.LastName, identity?.Username, identity?.Id ?? 0);
    }

    extension(ChatIdentity? identity)
    {
        /// <summary>
        /// Format chat identity for INFO logs (name only, no ID).
        /// </summary>
        public string ToLogInfo()
            => LogDisplayName.ChatInfo(identity?.ChatName, identity?.Id ?? 0);

        /// <summary>
        /// Format chat identity for DEBUG/WARNING/ERROR logs (name + ID).
        /// </summary>
        public string ToLogDebug()
            => LogDisplayName.ChatDebug(identity?.ChatName, identity?.Id ?? 0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Web User Identity Extensions (WebUserIdentity)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(WebUserIdentity? identity)
    {
        /// <summary>
        /// Format web user identity for INFO logs (email only, no ID).
        /// </summary>
        public string ToLogInfo()
            => LogDisplayName.WebUserInfo(identity?.Email, identity?.Id);

        /// <summary>
        /// Format web user identity for DEBUG/WARNING/ERROR logs (email + ID).
        /// </summary>
        public string ToLogDebug()
            => LogDisplayName.WebUserDebug(identity?.Email, identity?.Id ?? "unknown");
    }

    extension(WebUserIdentity identity)
    {
        /// <summary>
        /// Convert web user identity to Actor for audit logging.
        /// </summary>
        public Actor ToActor() => Actor.FromWebUser(identity.Id, identity.Email);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Web User Record Extensions (UserRecord — delegates to embedded WebUser)
    // ═══════════════════════════════════════════════════════════════════════════

    extension(UserRecord? user)
    {
        /// <summary>
        /// Format web user for INFO logs (email only, no ID).
        /// </summary>
        public string ToLogInfo()
            => user?.WebUser.ToLogInfo() ?? LogDisplayName.WebUserInfo(null, null);

        /// <summary>
        /// Format web user for DEBUG/WARNING/ERROR logs (email + ID).
        /// </summary>
        public string ToLogDebug()
            => user?.WebUser.ToLogDebug() ?? LogDisplayName.WebUserDebug(null, "unknown");

        /// <summary>
        /// Format web user for INFO logs with userId fallback when user is null.
        /// </summary>
        public string ToLogInfo(string userId)
            => LogDisplayName.WebUserInfo(user?.WebUser.Email, userId);

        /// <summary>
        /// Format web user for DEBUG/WARNING/ERROR logs with userId fallback when user is null.
        /// </summary>
        public string ToLogDebug(string userId)
            => LogDisplayName.WebUserDebug(user?.WebUser.Email, userId);
    }
}
