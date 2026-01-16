namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Impersonation alert moderation actions for DM callback buttons.
/// Int values used in callback data for compact encoding.
/// </summary>
public enum ImpersonationAction
{
    /// <summary>Confirm as scammer, ban user</summary>
    ConfirmScam = 0,

    /// <summary>Mark as false positive, restore permissions</summary>
    FalsePositive = 1,

    /// <summary>Add user to whitelist for this target</summary>
    Whitelist = 2
}
