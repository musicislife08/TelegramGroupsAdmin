namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// What caused a profile scan to be considered. Selects which config flag
/// applies and whether the never-scanned condition is enforced.
/// </summary>
public enum ProfileScanTrigger
{
    /// <summary>User joined a chat. Scans a new or never-scanned user, or one who renamed since the last scan.</summary>
    Join,

    /// <summary>
    /// User sent a message. Scans a never-scanned user (covers accounts that arrive without a join
    /// event, such as people commenting on channel posts in a linked discussion group), or one who
    /// renamed since the last scan.
    /// </summary>
    FirstMessage,

    /// <summary>A rename was observed. Always scans, even an excluded user.</summary>
    ProfileChange
}
