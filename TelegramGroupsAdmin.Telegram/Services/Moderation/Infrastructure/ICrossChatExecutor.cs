namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

/// <summary>
/// Executes actions across all healthy managed chats in parallel.
/// Pure infrastructure - callers provide the action to perform per chat.
/// Handles health filtering and aggregates success/fail counts.
/// </summary>
public interface ICrossChatExecutor
{
    /// <summary>
    /// Execute an action across all active, healthy managed chats.
    /// </summary>
    /// <param name="action">Action to execute per chat (receives chatId, cancellationToken).</param>
    /// <param name="actionName">Name for logging purposes (e.g., "Ban", "Unban").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing success/fail/skipped counts.</returns>
    Task<CrossChatResult> ExecuteAcrossChatsAsync(
        Func<long, CancellationToken, Task> action,
        string actionName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of cross-chat execution.
/// </summary>
/// <param name="SuccessCount">Number of chats where the action succeeded.</param>
/// <param name="FailCount">Number of chats where the action failed.</param>
/// <param name="SkippedCount">Number of unhealthy chats that were skipped.</param>
public record CrossChatResult(int SuccessCount, int FailCount, int SkippedCount);
