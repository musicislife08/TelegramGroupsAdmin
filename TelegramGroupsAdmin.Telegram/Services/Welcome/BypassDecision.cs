namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Reason a welcome-flow bypass fired for a joining user.
/// </summary>
public enum BypassDecision
{
    /// <summary>No bypass — user proceeds through the normal welcome flow.</summary>
    None = 0,

    /// <summary>
    /// User is admin-identified: either a Telegram chat admin/creator in any tracked chat,
    /// or a linked web admin at GlobalAdmin or Owner permission level.
    /// </summary>
    Admin = 1,

    /// <summary>
    /// User has <c>IsTrusted = true</c> and the per-chat trusted-bypass toggle is enabled.
    /// </summary>
    Trusted = 2,
}
