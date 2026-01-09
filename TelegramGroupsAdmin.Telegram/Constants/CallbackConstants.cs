namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Centralized constants for Telegram callback query prefixes.
/// Callback data format: "{prefix}:{payload}"
/// </summary>
public static class CallbackConstants
{
    /// <summary>
    /// Prefix for ban user selection callbacks.
    /// Format: ban_select:{userId}:{commandMessageId}
    /// </summary>
    public const string BanSelectPrefix = "ban_select:";

    /// <summary>
    /// Prefix for ban selection cancel callbacks.
    /// Format: ban_cancel:{commandMessageId}
    /// </summary>
    public const string BanCancelPrefix = "ban_cancel:";
}
