namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Notification delivery channels
/// Values stored as integers in JSONB config
/// </summary>
public enum NotificationChannel
{
    /// <summary>Telegram Direct Message via linked account</summary>
    TelegramDm = 0,

    /// <summary>Email to user's validated account email</summary>
    Email = 1,

    /// <summary>In-app notifications (bell icon + browser push)</summary>
    WebPush = 2
}
