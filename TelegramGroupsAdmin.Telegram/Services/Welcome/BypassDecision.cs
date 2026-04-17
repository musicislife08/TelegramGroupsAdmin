namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Reason a welcome-flow bypass fired for a joining user.
/// </summary>
public enum BypassDecision
{
    /// <summary>No bypass — user proceeds through normal welcome flow.</summary>
    None = 0,

    /// <summary>User is a Telegram chat administrator or creator.</summary>
    ChatAdmin = 1,

    /// <summary>User is linked to a web admin with GlobalAdmin or Owner permission level.</summary>
    WebAdmin = 2,

    /// <summary>User has IsTrusted = true and the per-chat bypass toggle is enabled.</summary>
    Trusted = 3,
}
