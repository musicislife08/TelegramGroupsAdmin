namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Moderation action types for user management (stored as INT in database)
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
    RemoveWarning = 7
}
