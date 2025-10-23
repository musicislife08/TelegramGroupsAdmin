namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Telegram user status classification for display badges
/// </summary>
public enum TelegramUserStatus
{
    /// <summary>No issues - blue badge</summary>
    Clean = 0,

    /// <summary>Has tags or notes for tracking - yellow badge</summary>
    Tagged = 1,

    /// <summary>Has warnings - orange badge</summary>
    Warned = 2,

    /// <summary>Banned - red badge</summary>
    Banned = 3,

    /// <summary>Explicitly trusted - green badge</summary>
    Trusted = 4
}
