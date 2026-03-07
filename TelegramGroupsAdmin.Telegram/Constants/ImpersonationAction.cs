namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Impersonation alert moderation actions for DM callback buttons.
/// Int values used in callback data for compact encoding.
/// </summary>
public enum ImpersonationAction
{
    /// <summary>Confirm as scammer, ban user</summary>
    Confirm = 0,

    /// <summary>Dismiss alert as false positive, no action</summary>
    Dismiss = 1,

    /// <summary>Trust user (prevents future impersonation alerts)</summary>
    Trust = 2
}
