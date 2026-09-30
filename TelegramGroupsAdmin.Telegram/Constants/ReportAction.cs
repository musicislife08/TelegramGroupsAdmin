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

    /// <summary>Acknowledge the report; the message's verdict is left unchanged</summary>
    Dismiss = 3,

    /// <summary>Mark the message clean (admin-confirmed not spam), trains the classifier</summary>
    Clean = 4
}
