namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Marker interface for moderation action results.
/// Results are immutable data objects containing the outcome of an action.
/// </summary>
public interface IActionResult
{
    /// <summary>Whether the action completed successfully.</summary>
    bool Success { get; }

    /// <summary>Error message if the action failed.</summary>
    string? ErrorMessage { get; }
}
