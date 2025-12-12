namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a ban action.
/// </summary>
/// <param name="Success">Whether the ban succeeded in at least one chat.</param>
/// <param name="ChatsAffected">Number of chats where ban succeeded.</param>
/// <param name="ChatsFailed">Number of chats where ban failed.</param>
/// <param name="ShouldRevokeTrust">Whether the orchestrator should revoke the user's trust.</param>
/// <param name="ErrorMessage">Error message if the action failed completely.</param>
public record BanResult(
    bool Success,
    int ChatsAffected,
    int ChatsFailed,
    bool ShouldRevokeTrust,
    string? ErrorMessage = null) : IActionResult
{
    /// <summary>
    /// Create a successful ban result.
    /// </summary>
    public static BanResult Succeeded(int chatsAffected, int chatsFailed = 0) =>
        new(true, chatsAffected, chatsFailed, ShouldRevokeTrust: true);

    /// <summary>
    /// Create a failed ban result.
    /// </summary>
    public static BanResult Failed(string errorMessage) =>
        new(false, 0, 0, ShouldRevokeTrust: false, errorMessage);
}
