namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a restrict (mute) action.
/// </summary>
/// <param name="Success">Whether the restriction succeeded in at least one chat.</param>
/// <param name="ChatsAffected">Number of chats where restriction succeeded.</param>
/// <param name="ChatsFailed">Number of chats where restriction failed.</param>
/// <param name="ExpiresAt">When the restriction will automatically expire.</param>
/// <param name="ErrorMessage">Error message if the action failed completely.</param>
public record RestrictResult(
    bool Success,
    int ChatsAffected,
    int ChatsFailed,
    DateTimeOffset ExpiresAt,
    string? ErrorMessage = null) : IActionResult
{
    /// <summary>
    /// Create a successful restrict result.
    /// </summary>
    public static RestrictResult Succeeded(int chatsAffected, DateTimeOffset expiresAt, int chatsFailed = 0) =>
        new(true, chatsAffected, chatsFailed, expiresAt);

    /// <summary>
    /// Create a failed restrict result.
    /// </summary>
    public static RestrictResult Failed(string errorMessage) =>
        new(false, 0, 0, DateTimeOffset.MinValue, errorMessage);
}
