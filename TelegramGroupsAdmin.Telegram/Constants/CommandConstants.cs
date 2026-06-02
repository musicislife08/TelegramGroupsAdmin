using TelegramGroupsAdmin.Core.Models;

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

    /// <summary>Tier whose command set is registered in the default (all-users) Telegram scope.</summary>
    public const PermissionLevel DefaultCommandPermissionLevel = PermissionLevel.Member;

    /// <summary>Tier whose command set is registered in the group-admin Telegram scope.</summary>
    public const PermissionLevel AdminCommandPermissionLevel = PermissionLevel.Admin;
}
