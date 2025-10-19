namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// User response status to welcome message prompts
/// </summary>
public enum WelcomeResponseType
{
    /// <summary>User has not yet responded to welcome message</summary>
    Pending = 0,

    /// <summary>User accepted the rules and is allowed in chat</summary>
    Accepted = 1,

    /// <summary>User declined the rules and was removed</summary>
    Denied = 2,

    /// <summary>User did not respond within timeout period and was removed</summary>
    Timeout = 3,

    /// <summary>User left the chat before responding</summary>
    Left = 4
}
