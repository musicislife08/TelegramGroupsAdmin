namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// User permission level hierarchy
/// </summary>
public enum PermissionLevel
{
    /// <summary>Can view data but cannot modify settings</summary>
    ReadOnly = 0,

    /// <summary>Can modify settings and take moderation actions</summary>
    Admin = 1,

    /// <summary>Full system access including user management</summary>
    Owner = 2
}
