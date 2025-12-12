using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;

/// <summary>
/// Intent to ban a user globally across all managed chats.
/// </summary>
/// <param name="UserId">User to ban.</param>
/// <param name="MessageId">Optional message ID for audit context.</param>
/// <param name="Executor">Who requested this action.</param>
/// <param name="Reason">Reason for the ban.</param>
public record BanIntent(
    long UserId,
    long? MessageId,
    Actor Executor,
    string Reason) : IActionIntent;
