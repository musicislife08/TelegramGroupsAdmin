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

    // TODO: Remove ReportActionPrefix after 2026-02-01 (see GitHub issue #281)
    // Legacy prefix kept temporarily for existing DM buttons that haven't expired yet.
    // DM callback contexts expire after 7 days, so this can be removed ~2 weeks after
    // the unified ReportCallbackHandler is deployed.
    /// <summary>
    /// Prefix for report moderation action callbacks (legacy, kept for existing DM buttons).
    /// Format: rpt:{contextId}:{actionInt}
    /// Uses compact format to stay within Telegram's 64-byte callback data limit.
    /// New code should use ReportActionPrefix.
    /// </summary>
    public const string ReportActionPrefix = "rpt:";

    /// <summary>
    /// Prefix for unified review action callbacks.
    /// Format: rev:{contextId}:{actionInt}
    /// Action meaning depends on ReviewType stored in context.
    /// </summary>
    public const string ReviewActionPrefix = "rev:";
}
