namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Reason why spam detection was skipped for a message (Telegram layer)
/// Maps to Data.Models.SpamCheckSkipReason via ModelMappings
/// Set by ContentCheckCoordinator based on user trust/admin status
/// </summary>
public enum SpamCheckSkipReason
{
    /// <summary>
    /// Spam check ran normally (not skipped)
    /// </summary>
    NotSkipped = 0,

    /// <summary>
    /// User is explicitly trusted (auto-trust or manual)
    /// Regular spam checks bypassed, critical checks still run
    /// </summary>
    UserTrusted = 1,

    /// <summary>
    /// User is a chat administrator
    /// Regular spam checks bypassed, critical checks still run
    /// </summary>
    UserAdmin = 2
}
