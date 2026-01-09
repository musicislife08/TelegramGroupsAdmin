namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Centralized constants for bot command defaults and limits.
/// </summary>
public static class CommandConstants
{
    /// <summary>
    /// Default temp ban duration when not specified (1 hour)
    /// </summary>
    public static readonly TimeSpan DefaultTempBanDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// Default mute duration when not specified (5 minutes)
    /// </summary>
    public static readonly TimeSpan DefaultMuteDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Minimum permission level for default commands (regular users)
    /// </summary>
    public const int DefaultCommandPermissionLevel = 0;

    /// <summary>
    /// Minimum permission level for admin commands
    /// </summary>
    public const int AdminCommandPermissionLevel = 1;
}
