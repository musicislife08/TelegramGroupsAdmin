using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;

/// <summary>
/// Intent to delete a specific message.
/// </summary>
/// <param name="MessageId">Message to delete.</param>
/// <param name="ChatId">Chat containing the message.</param>
/// <param name="UserId">User who sent the message (for audit).</param>
/// <param name="Executor">Who requested this action.</param>
/// <param name="Reason">Optional reason for deletion.</param>
public record DeleteIntent(
    long MessageId,
    long ChatId,
    long UserId,
    Actor Executor,
    string? Reason) : IActionIntent
{
    // IActionIntent requires non-nullable Reason, provide default
    string IActionIntent.Reason => Reason ?? "Manual message deletion";
}
