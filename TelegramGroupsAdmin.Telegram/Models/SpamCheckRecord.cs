namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Spam check record for UI display
/// </summary>
public record SpamCheckRecord(
    long Id,
    DateTimeOffset CheckTimestamp,
    long UserId,
    string? ContentHash,
    bool IsSpam,
    int Confidence,
    string? Reason,
    string CheckType,
    long? MatchedMessageId
);
