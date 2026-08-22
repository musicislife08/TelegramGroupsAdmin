using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for closing a user's still-open reports after a moderation action.
/// Owns no policy: the orchestrator decides when to call it and at what scope.
/// Does NOT know about bans, welcome flows, or notifications.
/// </summary>
public interface IReportCleanupHandler
{
    /// <summary>
    /// Close every pending report whose subject is <paramref name="user"/>.
    /// </summary>
    /// <param name="user">The report subject.</param>
    /// <param name="chat">Null closes reports in every chat; a value narrows to that chat.</param>
    /// <param name="executor">Recorded as the reviewer on each closed report.</param>
    /// <param name="actionName">Action label, e.g. "Ban" or "Kick". Stored as "Auto-{actionName}".</param>
    /// <param name="excludeReportId">
    /// Report that triggered the action, if any. It is skipped so the calling handler keeps
    /// ownership of its own status update and does not lose the race to this cleanup.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of reports actually closed.</returns>
    Task<int> CloseOpenReportsAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        string actionName,
        long? excludeReportId,
        CancellationToken cancellationToken = default);
}
