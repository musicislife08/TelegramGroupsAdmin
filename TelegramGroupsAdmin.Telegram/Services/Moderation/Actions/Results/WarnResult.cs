namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a warn action.
/// </summary>
/// <param name="Success">Whether the warning was recorded.</param>
/// <param name="WarningCount">Total warning count after this warning.</param>
/// <param name="ErrorMessage">Error message if the action failed.</param>
public record WarnResult(
    bool Success,
    int WarningCount,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Create a successful warn result.
    /// </summary>
    public static WarnResult Succeeded(int warningCount) =>
        new(true, warningCount);

    /// <summary>
    /// Create a failed warn result.
    /// </summary>
    public static WarnResult Failed(string errorMessage) =>
        new(false, 0, errorMessage);
}
