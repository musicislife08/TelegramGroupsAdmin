namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a notification action.
/// Worker reports facts, orchestrator decides what to do with failures.
/// </summary>
/// <param name="Success">Whether the notification was sent successfully.</param>
/// <param name="ErrorMessage">Error message if the notification failed.</param>
public record NotificationResult(
    bool Success,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Create a successful notification result.
    /// </summary>
    public static NotificationResult Succeeded() =>
        new(true);

    /// <summary>
    /// Create a failed notification result.
    /// </summary>
    public static NotificationResult Failed(string errorMessage) =>
        new(false, errorMessage);
}
