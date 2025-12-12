namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a delete message action.
/// </summary>
/// <param name="Success">Whether the delete operation succeeded.</param>
/// <param name="MessageDeleted">Whether the message was actually deleted from Telegram.</param>
/// <param name="ErrorMessage">Error message if the action failed.</param>
public record DeleteResult(
    bool Success,
    bool MessageDeleted,
    string? ErrorMessage = null) : IActionResult
{
    /// <summary>
    /// Create a successful delete result.
    /// </summary>
    public static DeleteResult Succeeded(bool messageDeleted = true) =>
        new(true, messageDeleted);

    /// <summary>
    /// Create a failed delete result.
    /// </summary>
    public static DeleteResult Failed(string errorMessage) =>
        new(false, false, errorMessage);
}
