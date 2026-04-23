using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Decides whether a joining user bypasses the welcome flow.
/// Evaluates three rules in priority order: Telegram chat admin (any tracked chat),
/// linked web admin (GlobalAdmin/Owner), trusted user (toggle-gated).
/// Returns a <see cref="BypassResolution"/> carrying both the decision and a
/// human-readable reason detail suitable for audit persistence.
/// </summary>
public interface IWelcomeBypassResolver
{
    Task<BypassResolution> ResolveAsync(
        UserIdentity user,
        ChatIdentity chat,
        CancellationToken cancellationToken);
}
