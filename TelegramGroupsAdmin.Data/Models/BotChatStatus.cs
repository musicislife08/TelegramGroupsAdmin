namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Bot membership status in a Telegram chat (stored as INT in database)
/// </summary>
public enum BotChatStatus
{
    /// <summary>Bot is a regular member without admin privileges</summary>
    Member = 0,
    /// <summary>Bot is an administrator with elevated permissions</summary>
    Administrator = 1,
    /// <summary>Bot left the chat voluntarily</summary>
    Left = 2,
    /// <summary>Bot was kicked/removed from the chat</summary>
    Kicked = 3
}
