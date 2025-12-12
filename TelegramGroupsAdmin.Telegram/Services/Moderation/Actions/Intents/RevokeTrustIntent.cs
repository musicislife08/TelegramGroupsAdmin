using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;

/// <summary>
/// Intent to revoke a user's trust status.
/// Called when a user is banned or marked as spam to ensure compromised accounts lose trust.
/// </summary>
/// <param name="UserId">User whose trust to revoke.</param>
/// <param name="Executor">Who requested this action.</param>
/// <param name="Reason">Reason for revoking trust.</param>
public record RevokeTrustIntent(
    long UserId,
    Actor Executor,
    string Reason) : IActionIntent;
