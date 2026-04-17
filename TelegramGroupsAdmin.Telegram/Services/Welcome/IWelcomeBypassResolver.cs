using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Decides whether a joining user bypasses the welcome flow.
/// Evaluates three rules in priority order: Telegram chat admin, linked web admin, trusted user.
/// </summary>
public interface IWelcomeBypassResolver
{
    Task<BypassDecision> ResolveAsync(
        UserIdentity user,
        ChatIdentity chat,
        CancellationToken cancellationToken);
}
