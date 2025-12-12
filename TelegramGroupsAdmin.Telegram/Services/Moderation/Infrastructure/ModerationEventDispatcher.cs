using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Events;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

/// <summary>
/// Routes moderation events to registered handlers in order.
/// Catches exceptions from handlers to allow remaining handlers to execute.
/// </summary>
public class ModerationEventDispatcher : IModerationEventDispatcher
{
    private readonly IReadOnlyList<IModerationHandler> _handlers;
    private readonly ILogger<ModerationEventDispatcher> _logger;

    public ModerationEventDispatcher(
        IEnumerable<IModerationHandler> handlers,
        ILogger<ModerationEventDispatcher> logger)
    {
        // Sort handlers by Order at construction time
        _handlers = handlers.OrderBy(h => h.Order).ToList();
        _logger = logger;

        _logger.LogDebug(
            "Initialized dispatcher with {Count} handlers: {Handlers}",
            _handlers.Count,
            string.Join(", ", _handlers.Select(h => $"{h.GetType().Name}(Order={h.Order})")));
    }

    /// <inheritdoc />
    public async Task<ModerationDispatchResult> DispatchAsync(ModerationEvent evt, CancellationToken ct = default)
    {
        var result = new ModerationDispatchResult();

        foreach (var handler in _handlers)
        {
            // Skip if handler doesn't apply to this action type
            if (handler.AppliesTo.Length > 0 && !handler.AppliesTo.Contains(evt.ActionType))
            {
                continue;
            }

            try
            {
                _logger.LogDebug(
                    "Dispatching {ActionType} event for user {UserId} to {HandlerType}",
                    evt.ActionType, evt.UserId, handler.GetType().Name);

                var followUp = await handler.HandleAsync(evt, ct);

                // Capture first follow-up request
                if (followUp != ModerationFollowUp.None && result.FollowUp == ModerationFollowUp.None)
                {
                    result = result with
                    {
                        FollowUp = followUp,
                        FollowUpReason = $"Requested by {handler.GetType().Name}"
                    };

                    _logger.LogInformation(
                        "Handler {HandlerType} requested follow-up action {FollowUp} for user {UserId}",
                        handler.GetType().Name, followUp, evt.UserId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Handler {HandlerType} failed for {ActionType} on user {UserId}. Continuing with remaining handlers.",
                    handler.GetType().Name, evt.ActionType, evt.UserId);
                // Continue with remaining handlers - side-effects are independent
            }
        }

        return result;
    }
}
