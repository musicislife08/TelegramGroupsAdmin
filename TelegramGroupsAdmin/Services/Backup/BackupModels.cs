using MessagePack;

namespace TelegramGroupsAdmin.Services.Backup;

/// <summary>
/// Root backup container - includes all system data
/// </summary>
[MessagePackObject]
public class SystemBackup
{
    [Key(0)]
    public long CreatedAt { get; set; }

    [Key(1)]
    public string Version { get; set; } = "1.0";

    [Key(2)]
    public List<UserBackup> Users { get; set; } = [];

    [Key(3)]
    public List<InviteBackup> Invites { get; set; } = [];

    [Key(4)]
    public List<AuditLogBackup> AuditLogs { get; set; } = [];

    [Key(5)]
    public List<VerificationTokenBackup> VerificationTokens { get; set; } = [];

    [Key(6)]
    public List<MessageBackup> Messages { get; set; } = [];

    [Key(7)]
    public List<MessageEditBackup> MessageEdits { get; set; } = [];

    [Key(8)]
    public List<DetectionResultBackup> DetectionResults { get; set; } = [];

    [Key(9)]
    public List<UserActionBackup> UserActions { get; set; } = [];

    [Key(10)]
    public List<StopWordBackup> StopWords { get; set; } = [];

    [Key(11)]
    public List<SpamDetectionConfigBackup> SpamDetectionConfigs { get; set; } = [];

    [Key(12)]
    public List<SpamCheckConfigBackup> SpamCheckConfigs { get; set; } = [];

    [Key(13)]
    public List<ChatPromptBackup> ChatPrompts { get; set; } = [];

    [Key(14)]
    public List<ManagedChatBackup> ManagedChats { get; set; } = [];

    [Key(15)]
    public List<ChatAdminBackup> ChatAdmins { get; set; } = [];

    [Key(16)]
    public List<TelegramUserMappingBackup> TelegramUserMappings { get; set; } = [];

    [Key(17)]
    public List<TelegramLinkTokenBackup> TelegramLinkTokens { get; set; } = [];

    [Key(18)]
    public List<ReportBackup> Reports { get; set; } = [];
}

[MessagePackObject]
public class UserBackup
{
    [Key(0)] public string Id { get; set; } = string.Empty;
    [Key(1)] public string Email { get; set; } = string.Empty;
    [Key(2)] public string NormalizedEmail { get; set; } = string.Empty;
    [Key(3)] public string PasswordHash { get; set; } = string.Empty;
    [Key(4)] public string SecurityStamp { get; set; } = string.Empty;
    [Key(5)] public int PermissionLevel { get; set; }
    [Key(6)] public string? InvitedBy { get; set; }
    [Key(7)] public bool IsActive { get; set; }
    [Key(8)] public string? TotpSecret { get; set; }
    [Key(9)] public bool TotpEnabled { get; set; }
    [Key(10)] public long? TotpSetupStartedAt { get; set; }
    [Key(11)] public long CreatedAt { get; set; }
    [Key(12)] public long? LastLoginAt { get; set; }
    [Key(13)] public int Status { get; set; }
    [Key(14)] public string? ModifiedBy { get; set; }
    [Key(15)] public long? ModifiedAt { get; set; }
    [Key(16)] public bool EmailVerified { get; set; }
    [Key(17)] public string? EmailVerificationToken { get; set; }
    [Key(18)] public long? EmailVerificationTokenExpiresAt { get; set; }
    [Key(19)] public string? PasswordResetToken { get; set; }
    [Key(20)] public long? PasswordResetTokenExpiresAt { get; set; }
}

[MessagePackObject]
public class InviteBackup
{
    [Key(0)] public string Token { get; set; } = string.Empty;
    [Key(1)] public string CreatedBy { get; set; } = string.Empty;
    [Key(2)] public long CreatedAt { get; set; }
    [Key(3)] public long ExpiresAt { get; set; }
    [Key(4)] public string? UsedBy { get; set; }
    [Key(5)] public int PermissionLevel { get; set; }
    [Key(6)] public int Status { get; set; }
    [Key(7)] public long? ModifiedAt { get; set; }
}

[MessagePackObject]
public class AuditLogBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public int EventType { get; set; }
    [Key(2)] public long Timestamp { get; set; }
    [Key(3)] public string? ActorUserId { get; set; }
    [Key(4)] public string? TargetUserId { get; set; }
    [Key(5)] public string? Value { get; set; }
}

[MessagePackObject]
public class VerificationTokenBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string UserId { get; set; } = string.Empty;
    [Key(2)] public string TokenType { get; set; } = string.Empty;
    [Key(3)] public string Token { get; set; } = string.Empty;
    [Key(4)] public string? Value { get; set; }
    [Key(5)] public long ExpiresAt { get; set; }
    [Key(6)] public long CreatedAt { get; set; }
    [Key(7)] public long? UsedAt { get; set; }
}

[MessagePackObject]
public class MessageBackup
{
    [Key(0)] public long MessageId { get; set; }
    [Key(1)] public long ChatId { get; set; }
    [Key(2)] public long UserId { get; set; }
    [Key(3)] public string? UserName { get; set; }
    [Key(4)] public long Timestamp { get; set; }
    [Key(5)] public string? MessageText { get; set; }
    [Key(6)] public string? PhotoFileId { get; set; }
    [Key(7)] public int? PhotoFileSize { get; set; }
    [Key(8)] public string? PhotoLocalPath { get; set; }
    [Key(9)] public string? PhotoThumbnailPath { get; set; }
    [Key(10)] public string? Urls { get; set; }
    [Key(11)] public string? ContentHash { get; set; }
    [Key(12)] public string? ChatName { get; set; }
    [Key(13)] public long? EditDate { get; set; }
    [Key(14)] public long? DeletedAt { get; set; }
    [Key(15)] public string? DeletionSource { get; set; }
}

[MessagePackObject]
public class MessageEditBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public long MessageId { get; set; }
    [Key(2)] public long EditDate { get; set; }
    [Key(3)] public string? PreviousText { get; set; }
    [Key(4)] public string? PreviousContentHash { get; set; }
}

[MessagePackObject]
public class DetectionResultBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public long MessageId { get; set; }
    [Key(2)] public long DetectedAt { get; set; }
    [Key(3)] public string DetectionSource { get; set; } = string.Empty;
    [Key(4)] public string DetectionMethod { get; set; } = string.Empty;
    [Key(5)] public bool IsSpam { get; set; }
    [Key(6)] public int Confidence { get; set; }
    [Key(7)] public string? Reason { get; set; }
    [Key(8)] public string? AddedBy { get; set; }
    [Key(9)] public long UserId { get; set; }
    [Key(10)] public string MessageText { get; set; } = string.Empty;
}

[MessagePackObject]
public class UserActionBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public long UserId { get; set; }
    [Key(2)] public long[]? ChatIds { get; set; }
    [Key(3)] public int ActionType { get; set; }
    [Key(4)] public long? MessageId { get; set; }
    [Key(5)] public string? IssuedBy { get; set; }
    [Key(6)] public long IssuedAt { get; set; }
    [Key(7)] public long? ExpiresAt { get; set; }
    [Key(8)] public string? Reason { get; set; }
}

[MessagePackObject]
public class StopWordBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Word { get; set; } = string.Empty;
    [Key(2)] public int WordType { get; set; }
    [Key(3)] public long AddedDate { get; set; }
    [Key(4)] public string Source { get; set; } = string.Empty;
    [Key(5)] public bool Enabled { get; set; }
    [Key(6)] public string? AddedBy { get; set; }
    [Key(7)] public int DetectionCount { get; set; }
    [Key(8)] public long? LastDetectedDate { get; set; }
}

[MessagePackObject]
public class SpamDetectionConfigBackup
{
    [Key(0)] public string ChatId { get; set; } = string.Empty;
    [Key(1)] public int MinConfidenceThreshold { get; set; }
    [Key(2)] public string[]? EnabledChecks { get; set; }
    [Key(3)] public string? CustomPrompt { get; set; }
    [Key(4)] public int AutoBanThreshold { get; set; }
    [Key(5)] public long CreatedAt { get; set; }
    [Key(6)] public long? UpdatedAt { get; set; }
}

[MessagePackObject]
public class SpamCheckConfigBackup
{
    [Key(0)] public string CheckName { get; set; } = string.Empty;
    [Key(1)] public bool Enabled { get; set; }
    [Key(2)] public int ConfidenceWeight { get; set; }
    [Key(3)] public string? ConfigJson { get; set; }
    [Key(4)] public long? UpdatedAt { get; set; }
}

[MessagePackObject]
public class ChatPromptBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string ChatId { get; set; } = string.Empty;
    [Key(2)] public string Prompt { get; set; } = string.Empty;
    [Key(3)] public long CreatedAt { get; set; }
    [Key(4)] public long? UpdatedAt { get; set; }
}

[MessagePackObject]
public class ManagedChatBackup
{
    [Key(0)] public long ChatId { get; set; }
    [Key(1)] public string? ChatTitle { get; set; }
    [Key(2)] public string? ChatUsername { get; set; }
    [Key(3)] public string ChatType { get; set; } = string.Empty;
    [Key(4)] public long AddedAt { get; set; }
    [Key(5)] public bool IsActive { get; set; }
}

[MessagePackObject]
public class ChatAdminBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public long ChatId { get; set; }
    [Key(2)] public long TelegramUserId { get; set; }
    [Key(3)] public string? TelegramUsername { get; set; }
    [Key(4)] public long CachedAt { get; set; }
    [Key(5)] public bool IsActive { get; set; }
}

[MessagePackObject]
public class TelegramUserMappingBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public long TelegramId { get; set; }
    [Key(2)] public string? TelegramUsername { get; set; }
    [Key(3)] public string UserId { get; set; } = string.Empty;
    [Key(4)] public long LinkedAt { get; set; }
    [Key(5)] public bool IsActive { get; set; }
}

[MessagePackObject]
public class TelegramLinkTokenBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Token { get; set; } = string.Empty;
    [Key(2)] public string UserId { get; set; } = string.Empty;
    [Key(3)] public long CreatedAt { get; set; }
    [Key(4)] public long ExpiresAt { get; set; }
    [Key(5)] public bool Used { get; set; }
}

[MessagePackObject]
public class ReportBackup
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public long MessageId { get; set; }
    [Key(2)] public long ChatId { get; set; }
    [Key(3)] public long ReportedUserId { get; set; }
    [Key(4)] public string? ReportedUsername { get; set; }
    [Key(5)] public long ReportedBy { get; set; }
    [Key(6)] public string? ReporterUsername { get; set; }
    [Key(7)] public long ReportedAt { get; set; }
    [Key(8)] public string? Reason { get; set; }
    [Key(9)] public int Status { get; set; }
    [Key(10)] public string? ReviewedBy { get; set; }
    [Key(11)] public long? ReviewedAt { get; set; }
    [Key(12)] public string? ActionTaken { get; set; }
    [Key(13)] public string? MessageText { get; set; }
    [Key(14)] public string? MessagePhotoFileId { get; set; }
    [Key(15)] public string? ChatTitle { get; set; }
}
