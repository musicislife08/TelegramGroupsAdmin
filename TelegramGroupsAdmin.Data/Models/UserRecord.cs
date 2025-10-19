using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TelegramGroupsAdmin.Data.Attributes;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// User permission level hierarchy (stored as INT in database)
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
/// User account status lifecycle (stored as INT in database)
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
/// Invite token status lifecycle (stored as INT in database)
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
/// Audit log event types for tracking system and user activities (stored as INT in database)
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
/// EF Core entity for users table
/// </summary>
[Table("users")]
public class UserRecordDto
{
    [Key]
    [Column("id")]
    [MaxLength(450)]
    public string Id { get; set; } = string.Empty;

    [Column("email")]
    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Column("normalized_email")]
    [Required]
    [MaxLength(256)]
    public string NormalizedEmail { get; set; } = string.Empty;

    [Column("password_hash")]
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("security_stamp")]
    [Required]
    public string SecurityStamp { get; set; } = string.Empty;

    [Column("permission_level")]
    public PermissionLevel PermissionLevel { get; set; }

    [Column("invited_by")]
    public string? InvitedBy { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [ProtectedData]
    [Column("totp_secret")]
    public string? TotpSecret { get; set; }

    [Column("totp_enabled")]
    public bool TotpEnabled { get; set; }

    [Column("totp_setup_started_at")]
    public DateTimeOffset? TotpSetupStartedAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("last_login_at")]
    public DateTimeOffset? LastLoginAt { get; set; }

    [Column("status")]
    public UserStatus Status { get; set; }

    [Column("modified_by")]
    public string? ModifiedBy { get; set; }

    [Column("modified_at")]
    public DateTimeOffset? ModifiedAt { get; set; }

    [Column("email_verified")]
    public bool EmailVerified { get; set; }

    [Column("email_verification_token")]
    public string? EmailVerificationToken { get; set; }

    [Column("email_verification_token_expires_at")]
    public DateTimeOffset? EmailVerificationTokenExpiresAt { get; set; }

    [Column("password_reset_token")]
    public string? PasswordResetToken { get; set; }

    [Column("password_reset_token_expires_at")]
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(InvitedBy))]
    public virtual UserRecordDto? InvitedByUser { get; set; }

    public virtual ICollection<InviteRecordDto> CreatedInvites { get; set; } = [];
    public virtual ICollection<TelegramUserMappingRecordDto> TelegramMappings { get; set; } = [];
    public virtual ICollection<TelegramLinkTokenRecordDto> LinkTokens { get; set; } = [];
    public virtual ICollection<VerificationTokenDto> VerificationTokens { get; set; } = [];
    public virtual ICollection<RecoveryCodeRecordDto> RecoveryCodes { get; set; } = [];
    public virtual ICollection<ReportDto> Reports { get; set; } = [];

    // Helper properties (not mapped)
    [NotMapped]
    [JsonIgnore]
    public int PermissionLevelInt => (int)PermissionLevel;

    [NotMapped]
    [JsonIgnore]
    public bool CanLogin => Status == UserStatus.Active && EmailVerified;

    [NotMapped]
    [JsonIgnore]
    public bool IsPending => Status == UserStatus.Pending;
}

/// <summary>
/// EF Core entity for recovery_codes table
/// </summary>
[Table("recovery_codes")]
public class RecoveryCodeRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("code_hash")]
    [Required]
    public string CodeHash { get; set; } = string.Empty;

    [Column("used_at")]
    public DateTimeOffset? UsedAt { get; set; }

    // Navigation property
    [ForeignKey(nameof(UserId))]
    public virtual UserRecordDto? User { get; set; }
}

/// <summary>
/// EF Core entity for invites table
/// </summary>
[Table("invites")]
public class InviteRecordDto
{
    [Key]
    [Column("token")]
    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    [Column("created_by")]
    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("used_by")]
    public string? UsedBy { get; set; }

    [Column("permission_level")]
    public PermissionLevel PermissionLevel { get; set; }

    [Column("status")]
    public InviteStatus Status { get; set; }

    [Column("modified_at")]
    public DateTimeOffset? ModifiedAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(CreatedBy))]
    public virtual UserRecordDto? Creator { get; set; }

    [ForeignKey(nameof(UsedBy))]
    public virtual UserRecordDto? UsedByUser { get; set; }
}

/// <summary>
/// DTO for invite with creator and used-by emails (used in JOIN queries)
/// Not an EF entity - just a DTO for query results
/// </summary>
public class InviteWithCreatorDto
{
    public InviteRecordDto Invite { get; set; } = null!;
    public string CreatorEmail { get; set; } = string.Empty;
    public string? UsedByEmail { get; set; }
}

/// <summary>
/// EF Core entity for audit_log table
/// </summary>
[Table("audit_log")]
public class AuditLogRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("event_type")]
    public AuditEventType EventType { get; set; }

    [Column("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Column("actor_user_id")]
    public string? ActorUserId { get; set; }        // Who performed the action (null for system events)

    [Column("target_user_id")]
    public string? TargetUserId { get; set; }       // Who was affected (null if not user-specific)

    [Column("value")]
    public string? Value { get; set; }              // Context/relevant data (e.g., new status, permission level, etc.)
}
