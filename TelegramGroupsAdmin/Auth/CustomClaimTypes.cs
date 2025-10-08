namespace TelegramGroupsAdmin.Auth;

/// <summary>
/// Custom claim type constants to ensure consistency across authentication and authorization.
/// </summary>
public static class CustomClaimTypes
{
    /// <summary>
    /// Permission level claim (0=ReadOnly, 1=Admin, 2=Owner).
    /// </summary>
    public const string PermissionLevel = "PermissionLevel";
}
