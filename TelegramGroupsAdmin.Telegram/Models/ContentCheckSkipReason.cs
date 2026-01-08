namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Reason why content detection was skipped for a message (Telegram layer)
/// Maps to Data.Models.ContentCheckSkipReason via ModelMappings
/// Set by ContentCheckCoordinator based on user trust/admin status
/// </summary>
public enum ContentCheckSkipReason
{
    /// <summary>
    /// Content check ran normally (not skipped)
    /// </summary>
    NotSkipped = 0,

    /// <summary>
    /// User is explicitly trusted (auto-trust or manual)
    /// Regular content checks bypassed, critical checks still run
    /// </summary>
    UserTrusted = 1,

    /// <summary>
    /// User is a chat administrator
    /// Regular content checks bypassed, critical checks still run
    /// </summary>
    UserAdmin = 2,

    /// <summary>
    /// Service message (join, leave, title change, etc.)
    /// No content to check - stored for UI consistency with Telegram Desktop
    /// </summary>
    ServiceMessage = 3
}
