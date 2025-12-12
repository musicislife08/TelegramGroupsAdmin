namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a revoke trust action.
/// </summary>
/// <param name="Success">Whether the trust was revoked.</param>
/// <param name="ErrorMessage">Error message if the action failed.</param>
public record RevokeTrustResult(
    bool Success,
    string? ErrorMessage = null) : IActionResult
{
    /// <summary>
    /// Create a successful revoke trust result.
    /// </summary>
    public static RevokeTrustResult Succeeded() => new(true);

    /// <summary>
    /// Create a failed revoke trust result.
    /// </summary>
    public static RevokeTrustResult Failed(string errorMessage) => new(false, errorMessage);
}
