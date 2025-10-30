namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Telegram chat type classification (stored as INT in database)
/// </summary>
public enum ManagedChatType
{
    /// <summary>Private one-on-one chat</summary>
    Private = 0,
    /// <summary>Basic group chat (up to 200 members, auto-upgrades to supergroup at 200 or when certain features enabled)</summary>
    Group = 1,
    /// <summary>Supergroup chat (up to 200k members, advanced admin features)</summary>
    Supergroup = 2,
    /// <summary>Broadcast channel</summary>
    Channel = 3
}
