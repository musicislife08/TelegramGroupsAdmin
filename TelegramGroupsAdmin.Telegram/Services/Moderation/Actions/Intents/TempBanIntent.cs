using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;

/// <summary>
/// Intent to temporarily ban a user globally with automatic expiry.
/// </summary>
/// <param name="UserId">User to temp ban.</param>
/// <param name="MessageId">Optional message ID for audit context.</param>
/// <param name="Executor">Who requested this action.</param>
/// <param name="Reason">Reason for the temp ban.</param>
/// <param name="Duration">How long the ban should last.</param>
public record TempBanIntent(
    long UserId,
    long? MessageId,
    Actor Executor,
    string Reason,
    TimeSpan Duration) : IActionIntent;
