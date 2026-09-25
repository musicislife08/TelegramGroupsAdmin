using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// User-facing notifications: Telegram DMs to individual users who opted in (or who are the
/// subject of the event). No web-user preferences, email, or push — those are admin concerns
/// on <see cref="IAdminNotificationService"/>. Planned home for today's user DMs on the legacy
/// INotificationOrchestrator path when that path is retired.
/// </summary>
public interface IUserNotificationService
{
    /// <summary>
    /// Queues a ban celebration for delivery to the chat's DM subscribers. Returns once queued;
    /// subscribers are resolved and messaged in the background.
    /// </summary>
    ValueTask EnqueueBanCelebrationAsync(ChatIdentity chat, string caption, int gifId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the ban celebration straight to the banned user. Returns true when the DM was delivered.
    /// </summary>
    Task<bool> SendBanCelebrationToBannedUserAsync(ChatIdentity chat, UserIdentity bannedUser, string dmCaption, int gifId, CancellationToken cancellationToken = default);
}
