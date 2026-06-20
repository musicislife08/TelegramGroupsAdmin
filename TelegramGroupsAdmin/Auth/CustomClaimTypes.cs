namespace TelegramGroupsAdmin.Auth;

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
    /// Security stamp claim. Compared against the DB on every session revalidation;
    /// a mismatch (stamp rotated by password/TOTP/permission change) invalidates the session.
    /// </summary>
    public const string SecurityStamp = "SecurityStamp";
}
