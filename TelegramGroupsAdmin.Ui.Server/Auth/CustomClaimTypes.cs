namespace TelegramGroupsAdmin.Ui.Server.Auth;

/// <summary>
/// Custom claim type constants to ensure consistency across authentication and authorization.
/// </summary>
public static class CustomClaimTypes
{
    /// <summary>
    /// Permission level claim (0=Admin, 1=GlobalAdmin, 2=Owner).
    /// </summary>
    public const string PermissionLevel = "PermissionLevel";

    /// <summary>
    /// Security stamp claim - changes when password or TOTP is modified.
    /// Used to invalidate existing sessions after security-sensitive changes.
    /// </summary>
    public const string SecurityStamp = "SecurityStamp";
}
