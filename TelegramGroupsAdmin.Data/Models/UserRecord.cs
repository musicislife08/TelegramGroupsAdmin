using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TelegramGroupsAdmin.Data.Attributes;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// User permission level (Data layer - stored as INT in database)
/// </summary>
public enum PermissionLevel
{
    ReadOnly = 0,
    Admin = 1,
    Owner = 2
}

/// <summary>
/// User status (Data layer - stored as INT in database)
/// </summary>
public enum UserStatus
{
    Pending = 0,
    Active = 1,
    Disabled = 2,
    Deleted = 3
}

/// <summary>
/// Invite status (Data layer - stored as INT in database)
/// </summary>
public enum InviteStatus
{
    Pending = 0,
    Used = 1,
    Revoked = 2
}

/// <summary>
/// Audit event type (Data layer - stored as INT in database)
/// </summary>
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
    public int PermissionLevelInt => (int)PermissionLevel;

    [NotMapped]
    public bool CanLogin => Status == UserStatus.Active && EmailVerified;

    [NotMapped]
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
