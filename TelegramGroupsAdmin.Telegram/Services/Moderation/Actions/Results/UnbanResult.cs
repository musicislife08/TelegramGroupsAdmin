namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of an unban action.
/// </summary>
/// <param name="Success">Whether the unban succeeded in at least one chat.</param>
/// <param name="ChatsAffected">Number of chats where unban succeeded.</param>
/// <param name="ChatsFailed">Number of chats where unban failed.</param>
/// <param name="ErrorMessage">Error message if the action failed completely.</param>
public record UnbanResult(
    bool Success,
    int ChatsAffected,
    int ChatsFailed,
    string? ErrorMessage = null) : IActionResult
{
    /// <summary>
    /// Create a successful unban result.
    /// </summary>
    public static UnbanResult Succeeded(int chatsAffected, int chatsFailed = 0) =>
        new(true, chatsAffected, chatsFailed);

    /// <summary>
    /// Create a failed unban result.
    /// </summary>
    public static UnbanResult Failed(string errorMessage) =>
        new(false, 0, 0, errorMessage);
}
