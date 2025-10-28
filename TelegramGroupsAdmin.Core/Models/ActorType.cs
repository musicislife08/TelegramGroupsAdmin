namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Actor type classification for action attribution (Phase 4.19)
/// </summary>
public enum ActorType
{
    /// <summary>Web UI user (authenticated admin)</summary>
    WebUser = 0,

    /// <summary>Telegram user (via bot commands)</summary>
    TelegramUser = 1,

    /// <summary>System/automated action</summary>
    System = 2
}
