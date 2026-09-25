namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <summary>Outcome of <c>/dmcelebrations on</c>.</summary>
public enum DmCelebrationSubscribeResult
{
    /// <summary>The user can already receive bot DMs; celebrations start with the next ban.</summary>
    Subscribed,

    /// <summary>The subscription is saved and a start prompt was posted; DMs begin once the user starts the bot.</summary>
    AwaitingStart,

    /// <summary>
    /// The user is banned (known locally, even if this chat hasn't synced the ban yet); nothing was
    /// saved or posted, so a banned user can't make the bot mention them in the group.
    /// </summary>
    NotAllowed
}
