namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Health status values for managed chats.
/// Stored as integers in database for efficiency.
/// </summary>
public enum ChatHealthStatusType
{
    /// <summary>
    /// Health status has not been determined yet (default/cold start state)
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Bot is admin with all required permissions (delete messages, ban/restrict members)
    /// </summary>
    Healthy = 1,

    /// <summary>
    /// Bot has some issues (not admin, or missing some permissions)
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Bot cannot reach the chat (removed, kicked, or chat deleted)
    /// </summary>
    Error = 3,

    /// <summary>
    /// Health checks not applicable (e.g., private chats)
    /// </summary>
    NotApplicable = 4
}
