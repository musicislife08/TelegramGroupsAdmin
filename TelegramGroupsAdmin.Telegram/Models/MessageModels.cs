namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Message record for UI display
/// </summary>
public record MessageRecord(
    long MessageId,
    long UserId,
    string? UserName,
    string? FirstName,
    long ChatId,
    DateTimeOffset Timestamp,
    string? MessageText,
    string? PhotoFileId,
    int? PhotoFileSize,
    string? Urls,
    DateTimeOffset? EditDate,
    string? ContentHash,
    string? ChatName,
    string? PhotoLocalPath,
    string? PhotoThumbnailPath,
    string? ChatIconPath,
    string? UserPhotoPath,
    DateTimeOffset? DeletedAt,
    string? DeletionSource,
    long? ReplyToMessageId,
    string? ReplyToUser,
    string? ReplyToText
);

/// <summary>
/// Photo message record for UI display
/// </summary>
public record PhotoMessageRecord(
    string FileId,
    string? MessageText,
    DateTimeOffset Timestamp
);

/// <summary>
/// History statistics for UI display
/// </summary>
public record HistoryStats(
    int TotalMessages,
    int UniqueUsers,
    int PhotoCount,
    DateTimeOffset? OldestTimestamp,
    DateTimeOffset? NewestTimestamp
);

/// <summary>
/// Message edit record for UI display
/// </summary>
public record MessageEditRecord(
    long Id,
    long MessageId,
    string? OldText,
    string? NewText,
    DateTimeOffset EditDate,
    string? OldContentHash,
    string? NewContentHash
);

/// <summary>
/// Spam check record for UI display (legacy - for backward compatibility)
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
/// Training data statistics for UI display
/// Used by TrainingData.razor to show spam/ham balance
/// </summary>
public class TrainingDataStats
{
    public int TotalSamples { get; set; }
    public int SpamSamples { get; set; }
    public int HamSamples { get; set; }
    public double SpamPercentage { get; set; }
    public Dictionary<string, int> SamplesBySource { get; set; } = new();
}

/// <summary>
/// Detection result record for UI display (Phase 4.19: Now uses Actor for attribution)
/// </summary>
public class DetectionResultRecord
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public string DetectionSource { get; set; } = string.Empty;
    public string DetectionMethod { get; set; } = string.Empty;
    public bool IsSpam { get; set; }
    public int Confidence { get; set; }
    public string? Reason { get; set; }

    /// <summary>
    /// Who added this detection (Phase 4.19: Actor system)
    /// </summary>
    public required Actor AddedBy { get; set; }

    public long UserId { get; set; }
    public string? MessageText { get; set; }
    public bool UsedForTraining { get; set; } = true;
    public int NetConfidence { get; set; }  // Required: computed column is_spam derives from this
    public string? CheckResultsJson { get; set; }  // Phase 2.6: JSON string with all check results
    public int EditVersion { get; set; }            // Phase 2.6: Message version (0 = original, 1+ = edits)
}

/// <summary>
/// User action record for UI display (bans, warns, mutes, trusts)
/// All actions are global - origin chat can be tracked via MessageId
/// Phase 4.19: Now uses Actor for attribution
/// </summary>
public record UserActionRecord(
    long Id,
    long UserId,
    UserActionType ActionType,
    long? MessageId,
    Actor IssuedBy,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    string? Reason
);

/// <summary>
/// Moderation action types for user management
/// </summary>
public enum UserActionType
{
    /// <summary>Ban user from all managed chats</summary>
    Ban = 0,

    /// <summary>Issue warning to user, counts toward auto-ban threshold</summary>
    Warn = 1,

    /// <summary>Temporarily mute user in chat</summary>
    Mute = 2,

    /// <summary>Mark user as trusted, bypass spam detection</summary>
    Trust = 3,

    /// <summary>Remove ban from user</summary>
    Unban = 4
}

/// <summary>
/// Bot membership status in a Telegram chat
/// </summary>
public enum BotChatStatus
{
    /// <summary>Bot is a regular member without admin privileges</summary>
    Member = 0,

    /// <summary>Bot is an administrator with elevated permissions</summary>
    Administrator = 1,

    /// <summary>Bot left the chat voluntarily</summary>
    Left = 2,

    /// <summary>Bot was kicked/removed from the chat</summary>
    Kicked = 3
}

/// <summary>
/// Telegram chat type classification
/// </summary>
public enum ManagedChatType
{
    /// <summary>Private one-on-one chat</summary>
    Private = 0,

    /// <summary>Basic group chat (legacy, up to 200 members)</summary>
    Group = 1,

    /// <summary>Supergroup chat (modern, unlimited members)</summary>
    Supergroup = 2,

    /// <summary>Broadcast channel</summary>
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
    DateTimeOffset AddedAt,
    bool IsActive,
    DateTimeOffset? LastSeenAt,
    string? SettingsJson,
    string? ChatIconPath
);

/// <summary>
/// Managed chat with health status information
/// </summary>
public class ManagedChatInfo
{
    public required ManagedChatRecord Chat { get; init; }
    public required ChatHealthStatus HealthStatus { get; set; }
    public bool HasCustomSpamConfig { get; set; }
    public bool HasCustomWelcomeConfig { get; set; }
}

/// <summary>
/// Health status for a chat (health-related info only, no chat metadata)
/// </summary>
public class ChatHealthStatus
{
    public long ChatId { get; set; }
    public bool IsReachable { get; set; }
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
