using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;

/// <summary>
/// Intent to restrict (mute) a user globally with automatic expiry.
/// </summary>
/// <param name="UserId">User to restrict.</param>
/// <param name="MessageId">Optional message ID for audit context.</param>
/// <param name="Executor">Who requested this action.</param>
/// <param name="Reason">Reason for the restriction.</param>
/// <param name="Duration">How long the restriction should last.</param>
public record RestrictIntent(
    long UserId,
    long? MessageId,
    Actor Executor,
    string Reason,
    TimeSpan Duration) : IActionIntent;
