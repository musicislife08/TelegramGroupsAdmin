namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a temp ban action.
/// </summary>
/// <param name="Success">Whether the temp ban succeeded in at least one chat.</param>
/// <param name="ChatsAffected">Number of chats where ban succeeded.</param>
/// <param name="ChatsFailed">Number of chats where ban failed.</param>
/// <param name="ExpiresAt">When the ban will automatically expire.</param>
/// <param name="ErrorMessage">Error message if the action failed completely.</param>
public record TempBanResult(
    bool Success,
    int ChatsAffected,
    int ChatsFailed,
    DateTimeOffset ExpiresAt,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Create a successful temp ban result.
    /// </summary>
    public static TempBanResult Succeeded(int chatsAffected, DateTimeOffset expiresAt, int chatsFailed = 0) =>
        new(true, chatsAffected, chatsFailed, expiresAt);

    /// <summary>
    /// Create a failed temp ban result.
    /// </summary>
    public static TempBanResult Failed(string errorMessage) =>
        new(false, 0, 0, DateTimeOffset.MinValue, errorMessage);
}
