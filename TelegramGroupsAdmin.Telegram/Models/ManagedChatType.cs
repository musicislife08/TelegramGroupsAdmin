namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Telegram chat type classification
/// </summary>
public enum ManagedChatType
{
    /// <summary>Private one-on-one chat</summary>
    Private = 0,

    /// <summary>Basic group chat (legacy, up to 200 members)</summary>
    Group = 1,

    /// <summary>Supergroup chat (modern, unlimited members)</summary>
    Supergroup = 2,

    /// <summary>Broadcast channel</summary>
    Channel = 3
}
