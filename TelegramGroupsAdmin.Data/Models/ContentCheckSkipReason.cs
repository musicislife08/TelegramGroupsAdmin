namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Reason why content detection was skipped for a message (database layer)
/// Maps to Telegram.Models.ContentCheckSkipReason via ModelMappings
/// </summary>
public enum ContentCheckSkipReason
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
