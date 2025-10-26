namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Reason why spam detection was skipped for a message (database layer)
/// Maps to Telegram.Models.SpamCheckSkipReason via ModelMappings
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
