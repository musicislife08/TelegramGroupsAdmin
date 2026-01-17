namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Moderation action types for user management
/// </summary>
public enum UserActionType
{
    /// <summary>Ban user from all managed chats</summary>
    Ban = 0,

    /// <summary>Issue warning to user, counts toward auto-ban threshold</summary>
    Warn = 1,

    /// <summary>Temporarily mute user in chat</summary>
    Mute = 2,

    /// <summary>Mark user as trusted, bypass spam detection</summary>
    Trust = 3,

    /// <summary>Remove ban from user</summary>
    Unban = 4,

    /// <summary>Remove trust from user</summary>
    Untrust = 5,

    /// <summary>Delete message (manual admin deletion)</summary>
    Delete = 6,

    /// <summary>Remove warning from user</summary>
    RemoveWarning = 7,

    /// <summary>Kick user from a specific chat (ban then immediate unban)</summary>
    Kick = 8,

    /// <summary>Restore user permissions to chat defaults (unmute)</summary>
    RestorePermissions = 9
}
