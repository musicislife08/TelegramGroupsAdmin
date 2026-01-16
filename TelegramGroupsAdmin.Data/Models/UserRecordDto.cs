using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TelegramGroupsAdmin.Data.Attributes;

namespace TelegramGroupsAdmin.Data.Models;

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

    [Column("locked_until")]
    public DateTimeOffset? LockedUntil { get; set; }

    [Column("failed_login_attempts")]
    public int FailedLoginAttempts { get; set; }

    // Navigation properties
    [ForeignKey(nameof(InvitedBy))]
    public virtual UserRecordDto? InvitedByUser { get; set; }

    public virtual ICollection<InviteRecordDto> CreatedInvites { get; set; } = [];
    public virtual ICollection<TelegramUserMappingRecordDto> TelegramMappings { get; set; } = [];
    public virtual ICollection<TelegramLinkTokenRecordDto> LinkTokens { get; set; } = [];
    public virtual ICollection<VerificationTokenDto> VerificationTokens { get; set; } = [];
    public virtual ICollection<RecoveryCodeRecordDto> RecoveryCodes { get; set; } = [];
    public virtual ICollection<ReviewDto> Reviews { get; set; } = [];

    // Helper properties (not mapped)
    [NotMapped]
    [JsonIgnore]
    public int PermissionLevelInt => (int)PermissionLevel;

    [NotMapped]
    [JsonIgnore]
    public bool CanLogin => Status == UserStatus.Active && EmailVerified && !IsLocked;

    [NotMapped]
    [JsonIgnore]
    public bool IsPending => Status == UserStatus.Pending;

    [NotMapped]
    [JsonIgnore]
    public bool IsLocked => LockedUntil.HasValue && LockedUntil.Value > DateTimeOffset.UtcNow;
}
