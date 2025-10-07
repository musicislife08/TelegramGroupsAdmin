namespace TelegramGroupsAdmin.Data.Models;

public record MessageRecord(
    long MessageId,
    long UserId,
    string? UserName,
    long ChatId,
    long Timestamp,
    long ExpiresAt,
    string? MessageText,
    string? PhotoFileId,
    int? PhotoFileSize,
    string? Urls,
    long? EditDate,
    string? ContentHash,
    string? ChatName,
    string? PhotoLocalPath,
    string? PhotoThumbnailPath
);

public record PhotoMessageRecord(
    string FileId,
    string? MessageText,
    long Timestamp
);

public record HistoryStats(
    long TotalMessages,
    long UniqueUsers,
    long PhotoCount,
    long? OldestTimestamp,
    long? NewestTimestamp
);

public record MessageEditRecord(
    long Id,
    long MessageId,
    long EditDate,
    string? OldText,
    string? NewText,
    string? OldContentHash,
    string? NewContentHash
);

public record SpamCheckRecord(
    long Id,
    long CheckTimestamp,
    long UserId,
    string? ContentHash,
    bool IsSpam,
    int Confidence,
    string? Reason,
    string CheckType,
    long? MatchedMessageId
);
