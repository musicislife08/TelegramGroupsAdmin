namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a mark as spam action.
/// </summary>
/// <param name="Success">Whether the action succeeded overall.</param>
/// <param name="ChatsAffected">Number of chats where ban succeeded.</param>
/// <param name="ChatsFailed">Number of chats where ban failed.</param>
/// <param name="MessageDeleted">Whether the message was deleted from Telegram.</param>
/// <param name="TrustRevoked">Whether the user's trust was revoked.</param>
/// <param name="ErrorMessage">Error message if the action failed completely.</param>
public record MarkAsSpamResult(
    bool Success,
    int ChatsAffected,
    int ChatsFailed,
    bool MessageDeleted,
    bool TrustRevoked,
    string? ErrorMessage = null) : IActionResult
{
    /// <summary>
    /// Create a successful mark as spam result.
    /// </summary>
    public static MarkAsSpamResult Succeeded(
        int chatsAffected,
        bool messageDeleted,
        bool trustRevoked,
        int chatsFailed = 0) =>
        new(true, chatsAffected, chatsFailed, messageDeleted, trustRevoked);

    /// <summary>
    /// Create a failed mark as spam result.
    /// </summary>
    public static MarkAsSpamResult Failed(string errorMessage) =>
        new(false, 0, 0, false, false, errorMessage);
}
