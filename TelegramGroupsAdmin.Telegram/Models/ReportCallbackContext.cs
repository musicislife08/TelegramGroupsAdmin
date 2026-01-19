using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for report moderation callback button context.
/// Action is passed in callback data, not stored here.
/// ReportType determines which action handler to use.
/// </summary>
public record ReportCallbackContext(
    long Id,
    long ReportId,
    ReportType ReportType,
    long ChatId,
    long UserId,
    DateTimeOffset CreatedAt);
