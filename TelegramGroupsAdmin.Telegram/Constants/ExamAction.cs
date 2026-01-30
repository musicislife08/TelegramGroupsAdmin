namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Exam failure review actions for DM callback buttons.
/// Int values used in callback data for compact encoding.
/// </summary>
public enum ExamAction
{
    /// <summary>Approve user - restore permissions, mark as active</summary>
    Approve = 0,

    /// <summary>Deny user - kick from chat</summary>
    Deny = 1,

    /// <summary>Deny and ban user from chat (prevents repeat join spam)</summary>
    DenyAndBan = 2
}
