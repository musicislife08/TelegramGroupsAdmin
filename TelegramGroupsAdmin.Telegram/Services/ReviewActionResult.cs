namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of a review action.
/// </summary>
public record ReviewActionResult(bool Success, string Message, string? ActionName = null);
