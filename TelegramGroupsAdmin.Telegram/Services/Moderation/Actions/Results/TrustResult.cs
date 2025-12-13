namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a trust action.
/// </summary>
/// <param name="Success">Whether the trust was set.</param>
/// <param name="ErrorMessage">Error message if the action failed.</param>
public record TrustResult(
    bool Success,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Create a successful trust result.
    /// </summary>
    public static TrustResult Succeeded() => new(true);

    /// <summary>
    /// Create a failed trust result.
    /// </summary>
    public static TrustResult Failed(string errorMessage) => new(false, errorMessage);
}
