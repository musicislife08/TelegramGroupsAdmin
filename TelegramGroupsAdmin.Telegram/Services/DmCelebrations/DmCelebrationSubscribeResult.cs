namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <summary>Outcome of <c>/dmcelebrations on</c>.</summary>
public enum DmCelebrationSubscribeResult
{
    /// <summary>The user can already receive bot DMs; celebrations start with the next ban.</summary>
    Subscribed,

    /// <summary>The subscription is saved and a start prompt was posted; DMs begin once the user starts the bot.</summary>
    AwaitingStart
}
