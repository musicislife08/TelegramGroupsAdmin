namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Where an observation of a user's names came from.
/// </summary>
public enum ObservationSource
{
    /// <summary>A message or edited message from the Bot API.</summary>
    BotUpdate = 0,
    /// <summary>A chat member update (join, leave, status change).</summary>
    ChatMember = 1,
    /// <summary>A profile fetched by the User API scan.</summary>
    UserApiScan = 2
}
