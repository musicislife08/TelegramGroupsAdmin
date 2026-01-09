using System.Collections.Frozen;

namespace TelegramGroupsAdmin.Core;

/// <summary>
/// Centralized Telegram-specific constants.
/// </summary>
public static class TelegramConstants
{
    /// <summary>
    /// Telegram's official service account user ID (777000).
    /// Used for channel posts forwarded to discussion groups and service messages.
    /// </summary>
    public const long ServiceAccountUserId = 777000;

    /// <summary>
    /// Telegram's anonymous group admin bot user ID (1087968824).
    /// Used when group admins post with "Remain Anonymous" enabled.
    /// Username: @GroupAnonymousBot
    /// </summary>
    public const long GroupAnonymousBotUserId = 1087968824;

    /// <summary>
    /// Telegram's channel bot user ID (136817688).
    /// Used for channel post signatures.
    /// Username: @Channel_Bot
    /// </summary>
    public const long ChannelBotUserId = 136817688;

    /// <summary>
    /// Telegram's replies bot user ID (1271266957).
    /// Used for reply headers in channels.
    /// Username: @replies
    /// </summary>
    public const long RepliesBotUserId = 1271266957;

    /// <summary>
    /// Telegram's antispam bot user ID (5434988373).
    /// Used by Telegram's aggressive antispam system.
    /// </summary>
    public const long AntispamBotUserId = 5434988373;

    /// <summary>
    /// All known Telegram system user IDs.
    /// These accounts should be trusted and exempt from moderation actions.
    /// Uses FrozenSet for immutable, high-performance O(1) lookups.
    /// </summary>
    /// <remarks>
    /// Sources for system user IDs:
    /// - 777000: Telegram Bot API changelog (service messages)
    /// - 1087968824: python-telegram-bot constants.py (GROUP_ANONYMOUS_BOT_ID)
    /// - 136817688: python-telegram-bot constants.py (CHANNEL_BOT_ID)
    /// - 1271266957: Telegram client observations (reply headers)
    /// - 5434988373: Telegram client observations (antispam system)
    /// See: https://github.com/python-telegram-bot/python-telegram-bot/blob/master/telegram/constants.py
    /// </remarks>
    private static readonly FrozenSet<long> SystemUserIds = new long[]
    {
        ServiceAccountUserId,      // 777000 - Service notifications, channel forwards
        GroupAnonymousBotUserId,   // 1087968824 - Anonymous group admins
        ChannelBotUserId,          // 136817688 - Channel signatures
        RepliesBotUserId,          // 1271266957 - Reply headers
        AntispamBotUserId          // 5434988373 - Telegram antispam
    }.ToFrozenSet();

    /// <summary>
    /// Check if a user ID belongs to a Telegram system account.
    /// System accounts are always trusted and exempt from moderation.
    /// </summary>
    /// <param name="userId">The Telegram user ID to check</param>
    /// <returns>True if this is a known system account</returns>
    public static bool IsSystemUser(long userId) => SystemUserIds.Contains(userId);

    /// <summary>
    /// Get a human-readable name for a system user ID.
    /// Returns null if the user ID is not a known system account.
    /// </summary>
    /// <param name="userId">The Telegram user ID</param>
    /// <returns>Display name or null</returns>
    public static string? GetSystemUserName(long userId) => userId switch
    {
        ServiceAccountUserId => "Telegram Service Account",
        GroupAnonymousBotUserId => "Anonymous Admin",
        ChannelBotUserId => "Channel Bot",
        RepliesBotUserId => "Replies Bot",
        AntispamBotUserId => "Telegram Antispam",
        _ => null
    };
}
