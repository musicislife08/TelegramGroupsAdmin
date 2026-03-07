namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Report moderation actions for DM callback buttons.
/// Int values used in callback data for compact encoding.
/// </summary>
public enum ReportAction
{
    /// <summary>Mark as spam, delete message, ban user</summary>
    Spam = 0,

    /// <summary>Delete message and ban user (not spam-classified)</summary>
    Ban = 1,

    /// <summary>Send warning to user</summary>
    Warn = 2,

    /// <summary>Dismiss report without action</summary>
    Dismiss = 3
}
