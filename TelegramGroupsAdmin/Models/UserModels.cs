namespace TelegramGroupsAdmin.Models;

/// <summary>
/// User record for UI display
/// </summary>
public record UserRecord(
    string Id,
    string Email,
    string NormalizedEmail,
    string PasswordHash,
    string SecurityStamp,
    PermissionLevel PermissionLevel,
    string? InvitedBy,
    bool IsActive,
    string? TotpSecret,
    bool TotpEnabled,
    long? TotpSetupStartedAt,
    long CreatedAt,
    long? LastLoginAt,
    UserStatus Status,
    string? ModifiedBy,
    long? ModifiedAt,
    bool EmailVerified,
    string? EmailVerificationToken,
    long? EmailVerificationTokenExpiresAt,
    string? PasswordResetToken,
    long? PasswordResetTokenExpiresAt
)
{
    public bool CanLogin => Status == UserStatus.Active && EmailVerified;
    public bool IsPending => Status == UserStatus.Pending;
    public int PermissionLevelInt => (int)PermissionLevel;
}

/// <summary>
/// Recovery code record for UI display
/// </summary>
public record RecoveryCodeRecord(
    long Id,
    string UserId,
    string CodeHash,
    long? UsedAt
);

/// <summary>
/// Invite record for UI display
/// </summary>
public record InviteRecord(
    string Token,
    string CreatedBy,
    long CreatedAt,
    long ExpiresAt,
    string? UsedBy,
    PermissionLevel PermissionLevel,
    InviteStatus Status,
    long? ModifiedAt
)
{
    public bool IsExpired => ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public bool IsValid => Status == InviteStatus.Pending && !IsExpired;
}

/// <summary>
/// Invite with creator information for UI display
/// </summary>
public record InviteWithCreator(
    string Token,
    string CreatedBy,
    string CreatedByEmail,
    long CreatedAt,
    long ExpiresAt,
    string? UsedBy,
    string? UsedByEmail,
    PermissionLevel PermissionLevel,
    InviteStatus Status,
    long? ModifiedAt
)
{
    public bool IsExpired => ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public bool IsValid => Status == InviteStatus.Pending && !IsExpired;
}

/// <summary>
/// Audit log record for UI display
/// </summary>
public record AuditLogRecord(
    long Id,
    AuditEventType EventType,
    long Timestamp,
    string? ActorUserId,
    string? TargetUserId,
    string? Value
);

public enum UserStatus
{
    Pending = 0,
    Active = 1,
    Disabled = 2,
    Deleted = 3
}

public enum InviteStatus
{
    Pending = 0,
    Used = 1,
    Revoked = 2
}

public enum PermissionLevel
{
    ReadOnly = 0,
    Admin = 1,
    Owner = 2
}

public enum AuditEventType
{
    // Data Operations
    DataExported = 0,
    MessageExported = 1,

    // System Events
    SystemConfigChanged = 2,

    // User Authentication
    UserEmailVerificationSent = 3,
    UserEmailVerified = 4,
    UserLogin = 5,
    UserLoginFailed = 6,
    UserLogout = 7,
    UserPasswordReset = 8,
    UserPasswordResetRequested = 9,

    // User Invites
    UserInviteCreated = 10,
    UserInviteRevoked = 11,

    // User Lifecycle
    UserDeleted = 12,
    UserRegistered = 13,
    UserStatusChanged = 14,

    // User Profile Changes
    UserEmailChanged = 15,
    UserPasswordChanged = 16,
    UserPermissionChanged = 17,
    UserTotpReset = 18,
    UserTotpEnabled = 19,
    UserAutoWhitelisted = 26,

    // Settings Changes (20-29 reserved for settings)
    SpamDetectionConfigChanged = 20,
    GeneralSettingsChanged = 21,
    TelegramSettingsChanged = 22,
    NotificationSettingsChanged = 23,
    SecuritySettingsChanged = 24,
    IntegrationSettingsChanged = 25
}

public enum InviteFilter
{
    Pending = 0,
    Used = 1,
    Revoked = 2,
    All = 3
}
