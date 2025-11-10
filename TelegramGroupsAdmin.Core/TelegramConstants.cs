namespace TelegramGroupsAdmin.Core;

/// <summary>
/// Centralized Telegram-specific constants.
/// </summary>
public static class TelegramConstants
{
    /// <summary>
    /// Telegram's official service account user ID.
    /// Used for channel posts, anonymous admin posts, and service messages.
    /// This user should always be trusted and exempt from moderation actions.
    /// </summary>
    public const long ServiceAccountUserId = 777000;
}
