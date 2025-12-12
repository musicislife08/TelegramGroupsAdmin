using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;

/// <summary>
/// Intent to mark a user as trusted (bypass spam detection).
/// </summary>
/// <param name="UserId">User to trust.</param>
/// <param name="Executor">Who requested this action.</param>
/// <param name="Reason">Reason for trusting the user.</param>
public record TrustIntent(
    long UserId,
    Actor Executor,
    string Reason) : IActionIntent;
