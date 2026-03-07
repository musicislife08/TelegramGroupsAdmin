namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Content check record for UI display
/// </summary>
public record ContentCheckRecord(
    DateTimeOffset CheckTimestamp,
    long UserId,
    bool IsSpam,
    double Score,
    string? Reason,
    int? MatchedMessageId
);
