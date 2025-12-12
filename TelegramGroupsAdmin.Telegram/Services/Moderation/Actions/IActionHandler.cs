namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles a specific moderation action intent and returns a result.
/// Action handlers are domain experts that own the execution of their specific action
/// (e.g., BanActionHandler owns Telegram ban API calls).
///
/// Unlike side-effect handlers (IModerationHandler), action handlers:
/// - Execute the primary action (e.g., call Telegram API)
/// - Return typed results with action-specific data
/// - Are called directly by the orchestrator, not via event dispatch
/// </summary>
/// <typeparam name="TIntent">The intent type this handler accepts.</typeparam>
/// <typeparam name="TResult">The result type this handler returns.</typeparam>
public interface IActionHandler<in TIntent, TResult>
    where TIntent : IActionIntent
    where TResult : IActionResult
{
    /// <summary>
    /// Execute the action described by the intent.
    /// </summary>
    /// <param name="intent">The action intent with all necessary parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing success/failure and action-specific data.</returns>
    Task<TResult> HandleAsync(TIntent intent, CancellationToken ct = default);
}
