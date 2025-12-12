using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;

/// <summary>
/// Intent to issue a warning to a user.
/// </summary>
/// <param name="UserId">User to warn.</param>
/// <param name="MessageId">Optional message ID for audit context.</param>
/// <param name="ChatId">Optional chat ID for chat-scoped warnings.</param>
/// <param name="Executor">Who requested this action.</param>
/// <param name="Reason">Reason for the warning.</param>
public record WarnIntent(
    long UserId,
    long? MessageId,
    long? ChatId,
    Actor Executor,
    string Reason) : IActionIntent;
