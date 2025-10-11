namespace TelegramGroupsAdmin.Data.Models;

public enum InviteStatus
{
    Pending = 0,
    Used = 1,
    Revoked = 2
}

// DTO for Dapper mapping from PostgreSQL
public record UserRecordDto
{
    public string id { get; init; } = string.Empty;
    public string email { get; init; } = string.Empty;
    public string normalized_email { get; init; } = string.Empty;
    public string password_hash { get; init; } = string.Empty;
    public string security_stamp { get; init; } = string.Empty;
    public int permission_level { get; init; }
    public string? invited_by { get; init; }
    public bool is_active { get; init; }
    public string? totp_secret { get; init; }
    public bool totp_enabled { get; init; }
    public long created_at { get; init; }
    public long? last_login_at { get; init; }
    public int status { get; init; }
    public string? modified_by { get; init; }
    public long? modified_at { get; init; }
    public bool email_verified { get; init; }
    public string? email_verification_token { get; init; }
    public long? email_verification_token_expires_at { get; init; }
    public string? password_reset_token { get; init; }
    public long? password_reset_token_expires_at { get; init; }
    public long? totp_setup_started_at { get; init; }

    // Map DTO to domain record
    public UserRecord ToUserRecord() => new UserRecord(
        Id: id,
        Email: email,
        NormalizedEmail: normalized_email,
        PasswordHash: password_hash,
        SecurityStamp: security_stamp,
        PermissionLevel: permission_level,
        InvitedBy: invited_by,
        IsActive: is_active,
        TotpSecret: totp_secret,
        TotpEnabled: totp_enabled,
        TotpSetupStartedAt: totp_setup_started_at,
        CreatedAt: created_at,
        LastLoginAt: last_login_at,
        Status: (UserStatus)status,
        ModifiedBy: modified_by,
        ModifiedAt: modified_at,
        EmailVerified: email_verified,
        EmailVerificationToken: email_verification_token,
        EmailVerificationTokenExpiresAt: email_verification_token_expires_at,
        PasswordResetToken: password_reset_token,
        PasswordResetTokenExpiresAt: password_reset_token_expires_at
    );
}

public record UserRecord(
    string Id,
    string Email,
    string NormalizedEmail,
    string PasswordHash,
    string SecurityStamp,
    int PermissionLevel,
    string? InvitedBy,
    bool IsActive, // Deprecated - kept for backward compatibility, use Status instead
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
    // Helper property to safely convert PermissionLevel to int
    public int PermissionLevelInt => (int)PermissionLevel;

    // Check if user can log in (Active status AND email verified)
    public bool CanLogin => Status == UserStatus.Active && EmailVerified;

    // Check if this is a placeholder/pending user (invited but not registered)
    public bool IsPending => Status == UserStatus.Pending;
};

// DTO for RecoveryCodeRecord
public record RecoveryCodeRecordDto
{
    public long id { get; init; }
    public string user_id { get; init; } = string.Empty;
    public string code_hash { get; init; } = string.Empty;
    public long? used_at { get; init; }

    public RecoveryCodeRecord ToRecoveryCodeRecord() => new RecoveryCodeRecord(
        Id: id,
        UserId: user_id,
        CodeHash: code_hash,
        UsedAt: used_at
    );
}

public record RecoveryCodeRecord(
    long Id,
    string UserId,
    string CodeHash,
    long? UsedAt
);

// DTO for InviteRecord
public record InviteRecordDto
{
    public string token { get; init; } = string.Empty;
    public string created_by { get; init; } = string.Empty;
    public long created_at { get; init; }
    public long expires_at { get; init; }
    public string? used_by { get; init; }
    public int permission_level { get; init; }
    public int status { get; init; }
    public long? modified_at { get; init; }

    public InviteRecord ToInviteRecord() => new InviteRecord(
        Token: token,
        CreatedBy: created_by,
        CreatedAt: created_at,
        ExpiresAt: expires_at,
        UsedBy: used_by,
        PermissionLevel: permission_level,
        Status: (InviteStatus)status,
        ModifiedAt: modified_at
    );
}

public record InviteRecord(
    string Token,
    string CreatedBy,
    long CreatedAt,
    long ExpiresAt,
    string? UsedBy,
    int PermissionLevel,
    InviteStatus Status,
    long? ModifiedAt
);

// DTO for InviteWithCreator (JOIN result)
public record InviteWithCreatorDto
{
    public string token { get; init; } = string.Empty;
    public string created_by { get; init; } = string.Empty;
    public long created_at { get; init; }
    public long expires_at { get; init; }
    public string? used_by { get; init; }
    public int permission_level { get; init; }
    public int status { get; init; }
    public long? modified_at { get; init; }
    public string? creator_email { get; init; }

    public InviteWithCreator ToInviteWithCreator() => new InviteWithCreator(
        Invite: new InviteRecord(
            Token: token,
            CreatedBy: created_by,
            CreatedAt: created_at,
            ExpiresAt: expires_at,
            UsedBy: used_by,
            PermissionLevel: permission_level,
            Status: (InviteStatus)status,
            ModifiedAt: modified_at
        ),
        CreatorEmail: creator_email ?? "Unknown"
    );
}

public record InviteWithCreator(
    InviteRecord Invite,
    string CreatorEmail
);

// DTO for AuditLogRecord
public record AuditLogRecordDto
{
    public long id { get; init; }
    public int event_type { get; init; }
    public long timestamp { get; init; }
    public string? actor_user_id { get; init; }
    public string? target_user_id { get; init; }
    public string? value { get; init; }

    public AuditLogRecord ToAuditLogRecord() => new AuditLogRecord(
        Id: id,
        EventType: (AuditEventType)event_type,
        Timestamp: timestamp,
        ActorUserId: actor_user_id,
        TargetUserId: target_user_id,
        Value: value
    );
}

public record AuditLogRecord(
    long Id,
    AuditEventType EventType,
    long Timestamp,
    string? ActorUserId,        // Who performed the action (null for system events)
    string? TargetUserId,       // Who was affected (null if not user-specific)
    string? Value               // Context/relevant data (e.g., new status, permission level, etc.)
);
