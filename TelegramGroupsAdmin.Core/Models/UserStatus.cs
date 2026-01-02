namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// User account status lifecycle
/// </summary>
public enum UserStatus
{
    /// <summary>Account created but email not verified</summary>
    Pending = 0,

    /// <summary>Account active and can login</summary>
    Active = 1,

    /// <summary>Account disabled by admin, cannot login</summary>
    Disabled = 2,

    /// <summary>Account marked for deletion, cannot login</summary>
    Deleted = 3
}
