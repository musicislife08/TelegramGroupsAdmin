namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Content check record for UI display
/// </summary>
public record ContentCheckRecord(
    long Id,
    DateTimeOffset CheckTimestamp,
    long UserId,
    string? ContentHash,
    bool IsSpam,
    double Score,
    string? Reason,
    string CheckType,
    int? MatchedMessageId
);
