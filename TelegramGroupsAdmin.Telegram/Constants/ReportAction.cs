namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Report moderation actions for DM callback buttons.
/// Int values used in callback data for compact encoding.
/// </summary>
public enum ReportAction
{
    /// <summary>Mark as spam, delete message, ban user</summary>
    Spam = 0,

    /// <summary>Send warning to user, mark report reviewed</summary>
    Warn = 1,

    /// <summary>Temporarily ban user, mark report reviewed</summary>
    TempBan = 2,

    /// <summary>Dismiss report without action</summary>
    Dismiss = 3
}
