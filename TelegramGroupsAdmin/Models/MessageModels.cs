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
    string? PhotoThumbnailPath,
    long? DeletedAt,
    string? DeletionSource
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
    public string? Reason { get; set; }
    public string? AddedBy { get; set; }
    public long UserId { get; set; }
    public string? MessageText { get; set; }
}

/// <summary>
/// User action record for UI display (bans, warns, mutes, trusts)
/// </summary>
public record UserActionRecord(
    long Id,
    long UserId,
    long[]? ChatIds,
    UserActionType ActionType,
    long? MessageId,
    string? IssuedBy,
    long IssuedAt,
    long? ExpiresAt,
    string? Reason
);

/// <summary>
/// Action types for user moderation
/// </summary>
public enum UserActionType
{
    Ban = 0,
    Warn = 1,
    Mute = 2,
    Trust = 3,
    Unban = 4
}

/// <summary>
/// Bot status in a managed chat
/// </summary>
public enum BotChatStatus
{
    Member = 0,
    Administrator = 1,
    Left = 2,
    Kicked = 3
}

/// <summary>
/// Chat type categories
/// </summary>
public enum ManagedChatType
{
    Private = 0,
    Group = 1,
    Supergroup = 2,
    Channel = 3
}

/// <summary>
/// Managed chat record for UI display
/// </summary>
public record ManagedChatRecord(
    long ChatId,
    string? ChatName,
    ManagedChatType ChatType,
    BotChatStatus BotStatus,
    bool IsAdmin,
    long AddedAt,
    bool IsActive,
    long? LastSeenAt,
    string? SettingsJson
);

/// <summary>
/// Managed chat with health status information
/// </summary>
public class ManagedChatInfo
{
    public required ManagedChatRecord Chat { get; init; }
    public required ChatHealthStatus HealthStatus { get; init; }
    public bool HasCustomSpamConfig { get; init; }
}

/// <summary>
/// Health status for a chat
/// </summary>
public class ChatHealthStatus
{
    public long ChatId { get; set; }
    public bool IsReachable { get; set; }
    public string? ChatTitle { get; set; }
    public string BotStatus { get; set; } = "Unknown";
    public bool IsAdmin { get; set; }
    public bool CanDeleteMessages { get; set; }
    public bool CanRestrictMembers { get; set; }
    public bool CanPromoteMembers { get; set; }
    public bool CanInviteUsers { get; set; }
    public int AdminCount { get; set; }
    public string Status { get; set; } = "Unknown";
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Bot permissions test result
/// </summary>
public class BotPermissionsTest
{
    public long ChatId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string BotStatus { get; set; } = "Unknown";
    public bool IsAdmin { get; set; }
    public bool CanDeleteMessages { get; set; }
    public bool CanRestrictMembers { get; set; }
    public bool CanPromoteMembers { get; set; }
    public bool CanInviteUsers { get; set; }
    public bool CanPinMessages { get; set; }
}
