namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// What caused a profile scan to be considered. Selects which config flag
/// applies and whether the never-scanned condition is enforced.
/// </summary>
public enum ProfileScanTrigger
{
    /// <summary>User joined a chat. Always rescans, ignoring prior scan history.</summary>
    Join,

    /// <summary>
    /// User sent a message and has never been scanned. Covers users who arrive
    /// without a join event, such as accounts commenting on channel posts in a
    /// linked discussion group.
    /// </summary>
    FirstMessage,

    /// <summary>Bot API profile fields changed. Always rescans.</summary>
    ProfileChange
}
