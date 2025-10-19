namespace TelegramGroupsAdmin.Telegram.Models;

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
    DateTimeOffset? TotpSetupStartedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    UserStatus Status,
    string? ModifiedBy,
    DateTimeOffset? ModifiedAt,
    bool EmailVerified,
    string? EmailVerificationToken,
    DateTimeOffset? EmailVerificationTokenExpiresAt,
    string? PasswordResetToken,
    DateTimeOffset? PasswordResetTokenExpiresAt
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
    DateTimeOffset? UsedAt
);

/// <summary>
/// Invite record for UI display
/// </summary>
public record InviteRecord(
    string Token,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? UsedBy,
    PermissionLevel PermissionLevel,
    InviteStatus Status,
    DateTimeOffset? ModifiedAt
)
{
    public bool IsExpired => ExpiresAt < DateTimeOffset.UtcNow;
    public bool IsValid => Status == InviteStatus.Pending && !IsExpired;
}

/// <summary>
/// Invite with creator information for UI display
/// </summary>
public record InviteWithCreator(
    string Token,
    string CreatedBy,
    string CreatedByEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? UsedBy,
    string? UsedByEmail,
    PermissionLevel PermissionLevel,
    InviteStatus Status,
    DateTimeOffset? ModifiedAt
)
{
    public bool IsExpired => ExpiresAt < DateTimeOffset.UtcNow;
    public bool IsValid => Status == InviteStatus.Pending && !IsExpired;
}

/// <summary>
/// Audit log record for UI display
/// </summary>
public record AuditLogRecord(
    long Id,
    AuditEventType EventType,
    DateTimeOffset Timestamp,
    string? ActorUserId,
    string? TargetUserId,
    string? Value
);

/// <summary>
/// User account status lifecycle
/// </summary>
public enum UserStatus
{
    /// <summary>Account created but email not verified</summary>
    Pending = 0,

    /// <summary>Account active and can login</summary>
    Active = 1,

    /// <summary>Account disabled by admin, cannot login</summary>
    Disabled = 2,

    /// <summary>Account marked for deletion, cannot login</summary>
    Deleted = 3
}

/// <summary>
/// Invite token status lifecycle
/// </summary>
public enum InviteStatus
{
    /// <summary>Invite available and unused</summary>
    Pending = 0,

    /// <summary>Invite redeemed by a user</summary>
    Used = 1,

    /// <summary>Invite cancelled by creator or admin</summary>
    Revoked = 2
}

/// <summary>
/// User permission level hierarchy
/// </summary>
public enum PermissionLevel
{
    /// <summary>Can view data but cannot modify settings</summary>
    ReadOnly = 0,

    /// <summary>Can modify settings and take moderation actions</summary>
    Admin = 1,

    /// <summary>Full system access including user management</summary>
    Owner = 2
}

/// <summary>
/// Audit log event types for tracking system and user activities
/// </summary>
public enum AuditEventType
{
    // Data Operations
    /// <summary>User exported data from the system</summary>
    DataExported = 0,

    /// <summary>User exported message history</summary>
    MessageExported = 1,

    // System Events
    /// <summary>System configuration was modified</summary>
    SystemConfigChanged = 2,

    // User Authentication
    /// <summary>Email verification sent to user</summary>
    UserEmailVerificationSent = 3,

    /// <summary>User verified their email address</summary>
    UserEmailVerified = 4,

    /// <summary>User logged in successfully</summary>
    UserLogin = 5,

    /// <summary>User login attempt failed</summary>
    UserLoginFailed = 6,

    /// <summary>User logged out</summary>
    UserLogout = 7,

    /// <summary>User password was reset</summary>
    UserPasswordReset = 8,

    /// <summary>User requested password reset</summary>
    UserPasswordResetRequested = 9,

    // User Invites
    /// <summary>New invite token created</summary>
    UserInviteCreated = 10,

    /// <summary>Invite token revoked</summary>
    UserInviteRevoked = 11,

    // User Lifecycle
    /// <summary>User account deleted</summary>
    UserDeleted = 12,

    /// <summary>New user registered</summary>
    UserRegistered = 13,

    /// <summary>User status changed</summary>
    UserStatusChanged = 14,

    // User Profile Changes
    /// <summary>User email address changed</summary>
    UserEmailChanged = 15,

    /// <summary>User password changed</summary>
    UserPasswordChanged = 16,

    /// <summary>User permission level changed</summary>
    UserPermissionChanged = 17,

    /// <summary>User TOTP/2FA reset</summary>
    UserTotpReset = 18,

    /// <summary>User enabled TOTP/2FA</summary>
    UserTotpEnabled = 19,

    /// <summary>User automatically whitelisted</summary>
    UserAutoWhitelisted = 26,

    // Settings Changes (20-29 reserved for settings)
    /// <summary>Spam detection configuration changed</summary>
    SpamDetectionConfigChanged = 20,

    /// <summary>General system settings changed</summary>
    GeneralSettingsChanged = 21,

    /// <summary>Telegram bot settings changed</summary>
    TelegramSettingsChanged = 22,

    /// <summary>Notification settings changed</summary>
    NotificationSettingsChanged = 23,

    /// <summary>Security settings changed</summary>
    SecuritySettingsChanged = 24,

    /// <summary>Integration settings changed</summary>
    IntegrationSettingsChanged = 25
}

/// <summary>
/// Filter options for querying invites by status
/// </summary>
public enum InviteFilter
{
    /// <summary>Show only pending invites</summary>
    Pending = 0,

    /// <summary>Show only used invites</summary>
    Used = 1,

    /// <summary>Show only revoked invites</summary>
    Revoked = 2,

    /// <summary>Show all invites regardless of status</summary>
    All = 3
}
