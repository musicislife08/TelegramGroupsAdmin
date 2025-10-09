namespace TelegramGroupsAdmin.Models;

/// <summary>
/// Message record for UI display
/// </summary>
public record MessageRecord(
    long MessageId,
    long UserId,
    string? UserName,
    long ChatId,
    long Timestamp,
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

/// <summary>
/// Photo message record for UI display
/// </summary>
public record PhotoMessageRecord(
    string FileId,
    string? MessageText,
    long Timestamp
);

/// <summary>
/// History statistics for UI display
/// </summary>
public record HistoryStats(
    int TotalMessages,
    int UniqueUsers,
    int PhotoCount,
    long? OldestTimestamp,
    long? NewestTimestamp
);

/// <summary>
/// Message edit record for UI display
/// </summary>
public record MessageEditRecord(
    long Id,
    long MessageId,
    string? OldText,
    string? NewText,
    long EditDate,
    string? OldContentHash,
    string? NewContentHash
);

/// <summary>
/// Spam check record for UI display (legacy - for backward compatibility)
/// </summary>
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

/// <summary>
/// Detection statistics for UI display
/// </summary>
public class DetectionStats
{
    public int TotalDetections { get; set; }
    public int SpamDetected { get; set; }
    public double SpamPercentage { get; set; }
    public double AverageConfidence { get; set; }
    public int Last24hDetections { get; set; }
    public int Last24hSpam { get; set; }
    public double Last24hSpamPercentage { get; set; }
}

/// <summary>
/// Detection result record for UI display
/// </summary>
public class DetectionResultRecord
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public long DetectedAt { get; set; }
    public string DetectionSource { get; set; } = string.Empty;
    public string DetectionMethod { get; set; } = string.Empty;
    public bool IsSpam { get; set; }
    public int Confidence { get; set; }
    public string? Details { get; set; }
    public long UserId { get; set; }
    public string? MessageText { get; set; }
}
