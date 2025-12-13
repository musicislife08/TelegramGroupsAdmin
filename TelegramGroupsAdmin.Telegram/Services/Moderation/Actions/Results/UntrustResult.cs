namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of an untrust action.
/// </summary>
/// <param name="Success">Whether the trust was removed.</param>
/// <param name="ErrorMessage">Error message if the action failed.</param>
public record UntrustResult(
    bool Success,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Create a successful untrust result.
    /// </summary>
    public static UntrustResult Succeeded() => new(true);

    /// <summary>
    /// Create a failed untrust result.
    /// </summary>
    public static UntrustResult Failed(string errorMessage) => new(false, errorMessage);
}
