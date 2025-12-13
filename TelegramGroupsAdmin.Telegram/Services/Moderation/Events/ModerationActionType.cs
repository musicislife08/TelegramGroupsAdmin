namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Events;

/// <summary>
/// Types of moderation actions that can be performed.
/// </summary>
public enum ModerationActionType
{
    /// <summary>Mark message as spam and ban the user globally.</summary>
    MarkAsSpamAndBan,

    /// <summary>Ban user globally across all managed chats.</summary>
    Ban,

    /// <summary>Issue a warning to the user.</summary>
    Warn,

    /// <summary>Mark user as trusted (bypass spam detection).</summary>
    Trust,

    /// <summary>Remove user's trusted status.</summary>
    Untrust,

    /// <summary>Unban user globally across all managed chats.</summary>
    Unban,

    /// <summary>Delete a single message.</summary>
    Delete,

    /// <summary>Temporarily ban user with auto-expiry.</summary>
    TempBan,

    /// <summary>Restrict (mute) user globally.</summary>
    Restrict
}
