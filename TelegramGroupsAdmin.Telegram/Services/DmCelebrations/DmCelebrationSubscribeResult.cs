namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <summary>Outcome of <c>/dmcelebrations on</c>.</summary>
public enum DmCelebrationSubscribeResult
{
    /// <summary>The confirmation was delivered by DM; celebrations start with the next ban.</summary>
    Subscribed,

    /// <summary>
    /// The subscription is saved but the user can't be DMed yet, so a start prompt was posted;
    /// DMs begin once the user starts the bot.
    /// </summary>
    AwaitingStart,

    /// <summary>
    /// The user is banned (known locally, even if this chat hasn't synced the ban yet); nothing was
    /// saved or posted, so a banned user can't make the bot mention them in the group. Also returned
    /// when the user has no user record (recording the sender failed), since nothing can be saved.
    /// </summary>
    NotAllowed
}
