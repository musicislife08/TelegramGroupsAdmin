using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;

/// <summary>
/// Intent to unban a user globally across all managed chats.
/// </summary>
/// <param name="UserId">User to unban.</param>
/// <param name="Executor">Who requested this action.</param>
/// <param name="Reason">Reason for the unban.</param>
/// <param name="RestoreTrust">Whether to also restore the user's trust status.</param>
public record UnbanIntent(
    long UserId,
    Actor Executor,
    string Reason,
    bool RestoreTrust = false) : IActionIntent;
