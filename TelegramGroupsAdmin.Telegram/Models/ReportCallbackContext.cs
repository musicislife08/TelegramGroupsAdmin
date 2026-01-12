namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for report moderation callback button context.
/// Action is passed in callback data, not stored here.
/// </summary>
public record ReportCallbackContext(
    long Id,
    long ReportId,
    long ChatId,
    long UserId,
    DateTimeOffset CreatedAt);
