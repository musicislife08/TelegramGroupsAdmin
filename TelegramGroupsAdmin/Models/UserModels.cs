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
    Login = 0,
    Logout = 1,
    PasswordChange = 2,
    TotpEnabled = 3,
    TotpDisabled = 4,
    UserCreated = 5,
    UserModified = 6,
    UserDeleted = 7,
    InviteCreated = 8,
    InviteUsed = 9,
    InviteRevoked = 10,
    PermissionChanged = 11,
    FailedLogin = 12,
    PasswordReset = 13,
    UserInviteCreated = 14,
    UserInviteRevoked = 15,
    UserPermissionChanged = 16,
    UserStatusChanged = 17,
    UserTotpDisabled = 18,
    DataExported = 19,
    UserTotpEnabled = 20,
    UserEmailChanged = 21,
    UserPasswordChanged = 22,
    UserLoginFailed = 23,
    UserLogout = 24,
    MessageExported = 25,
    UserLogin = 26,
    UserRegistered = 27,
    UserPasswordReset = 28,
    UserPasswordResetRequested = 29,
    UserEmailVerificationSent = 30,
    SystemConfigChanged = 31,
    UserEmailVerified = 32
}

public enum InviteFilter
{
    Pending = 0,
    Used = 1,
    Revoked = 2,
    All = 3
}
