using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TelegramGroupsAdmin.Data.Attributes;

namespace TelegramGroupsAdmin.Data.Models;

public enum InviteStatus
{
    Pending = 0,
    Used = 1,
    Revoked = 2
}

/// <summary>
/// EF Core entity for users table
/// </summary>
[Table("users")]
public class UserRecord
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
    public int PermissionLevel { get; set; }

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
    public long? TotpSetupStartedAt { get; set; }

    [Column("created_at")]
    public long CreatedAt { get; set; }

    [Column("last_login_at")]
    public long? LastLoginAt { get; set; }

    [Column("status")]
    public int Status { get; set; }

    [Column("modified_by")]
    public string? ModifiedBy { get; set; }

    [Column("modified_at")]
    public long? ModifiedAt { get; set; }

    [Column("email_verified")]
    public bool EmailVerified { get; set; }

    [Column("email_verification_token")]
    public string? EmailVerificationToken { get; set; }

    [Column("email_verification_token_expires_at")]
    public long? EmailVerificationTokenExpiresAt { get; set; }

    [Column("password_reset_token")]
    public string? PasswordResetToken { get; set; }

    [Column("password_reset_token_expires_at")]
    public long? PasswordResetTokenExpiresAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(InvitedBy))]
    public virtual UserRecord? InvitedByUser { get; set; }

    public virtual ICollection<InviteRecord> CreatedInvites { get; set; } = [];
    public virtual ICollection<TelegramUserMappingRecord> TelegramMappings { get; set; } = [];
    public virtual ICollection<TelegramLinkTokenRecord> LinkTokens { get; set; } = [];
    public virtual ICollection<VerificationToken> VerificationTokens { get; set; } = [];
    public virtual ICollection<RecoveryCodeRecord> RecoveryCodes { get; set; } = [];
    public virtual ICollection<Report> Reports { get; set; } = [];

    // Helper properties (not mapped)
    [NotMapped]
    public int PermissionLevelInt => (int)PermissionLevel;

    [NotMapped]
    public bool CanLogin => Status == (int)UserStatus.Active && EmailVerified;

    [NotMapped]
    public bool IsPending => Status == (int)UserStatus.Pending;
}

/// <summary>
/// EF Core entity for recovery_codes table
/// </summary>
[Table("recovery_codes")]
public class RecoveryCodeRecord
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
    public long? UsedAt { get; set; }

    // Navigation property
    [ForeignKey(nameof(UserId))]
    public virtual UserRecord? User { get; set; }
}

/// <summary>
/// EF Core entity for invites table
/// </summary>
[Table("invites")]
public class InviteRecord
{
    [Key]
    [Column("token")]
    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    [Column("created_by")]
    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("created_at")]
    public long CreatedAt { get; set; }

    [Column("expires_at")]
    public long ExpiresAt { get; set; }

    [Column("used_by")]
    public string? UsedBy { get; set; }

    [Column("permission_level")]
    public int PermissionLevel { get; set; }

    [Column("status")]
    public int Status { get; set; }

    [Column("modified_at")]
    public long? ModifiedAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(CreatedBy))]
    public virtual UserRecord? Creator { get; set; }

    [ForeignKey(nameof(UsedBy))]
    public virtual UserRecord? UsedByUser { get; set; }
}

/// <summary>
/// DTO for invite with creator email (used in JOIN queries)
/// Not an EF entity - just a DTO for query results
/// </summary>
public class InviteWithCreator
{
    public InviteRecord Invite { get; set; } = null!;
    public string CreatorEmail { get; set; } = string.Empty;
}

/// <summary>
/// EF Core entity for audit_log table
/// </summary>
[Table("audit_log")]
public class AuditLogRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("event_type")]
    public int EventType { get; set; }

    [Column("timestamp")]
    public long Timestamp { get; set; }

    [Column("actor_user_id")]
    public string? ActorUserId { get; set; }        // Who performed the action (null for system events)

    [Column("target_user_id")]
    public string? TargetUserId { get; set; }       // Who was affected (null if not user-specific)

    [Column("value")]
    public string? Value { get; set; }              // Context/relevant data (e.g., new status, permission level, etc.)
}
