namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// User account lifecycle status.
/// </summary>
public enum UserStatus
{
    /// <summary>
    /// User has been invited but hasn't completed registration yet.
    /// Invite exists but user record may not exist yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// User is active and can log in.
    /// </summary>
    Active = 1,

    /// <summary>
    /// User account is disabled (cannot log in).
    /// Can be re-enabled by admins.
    /// </summary>
    Disabled = 2,

    /// <summary>
    /// User account is soft-deleted (kept for audit purposes).
    /// Can be overwritten if the same email is invited again.
    /// </summary>
    Deleted = 3
}
