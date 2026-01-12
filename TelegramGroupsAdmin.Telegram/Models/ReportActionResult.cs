namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Result of a report moderation action (spam, warn, tempban, dismiss).
/// </summary>
/// <param name="Success">Whether the action completed successfully.</param>
/// <param name="Message">Human-readable result message for display in DM.</param>
public record ReportActionResult(bool Success, string Message);
