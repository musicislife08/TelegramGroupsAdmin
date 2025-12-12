using TelegramGroupsAdmin.Telegram.Services.Moderation.Events;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

/// <summary>
/// Dispatches moderation events to registered handlers.
/// </summary>
public interface IModerationEventDispatcher
{
    /// <summary>
    /// Dispatch an event to all applicable handlers.
    /// Handlers are executed in Order sequence. Exceptions are caught and logged.
    /// </summary>
    /// <param name="evt">The moderation event to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing any follow-up actions requested by handlers.</returns>
    Task<ModerationDispatchResult> DispatchAsync(ModerationEvent evt, CancellationToken ct = default);
}
