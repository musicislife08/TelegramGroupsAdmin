using TelegramGroupsAdmin.Telegram.Services.Moderation.Events;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Interface for moderation event handlers.
/// Handlers process side-effects after a moderation action is performed.
/// </summary>
public interface IModerationHandler
{
    /// <summary>
    /// Execution order (lower = earlier).
    /// Recommended values: 10=TrustRevocation, 20=WarningThreshold, 50=TrainingData, 100=AuditLog, 200=DmNotification
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Action types this handler applies to.
    /// Return empty array to handle all action types.
    /// </summary>
    ModerationActionType[] AppliesTo { get; }

    /// <summary>
    /// Process the moderation event.
    /// </summary>
    /// <param name="evt">The moderation event data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Optional follow-up action to request from the service.</returns>
    Task<ModerationFollowUp> HandleAsync(ModerationEvent evt, CancellationToken ct = default);
}
