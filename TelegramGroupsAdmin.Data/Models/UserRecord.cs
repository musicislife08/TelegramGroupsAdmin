namespace TelegramGroupsAdmin.Data.Models;

public enum InviteStatus
{
    Pending = 0,
    Used = 1,
    Revoked = 2
}

// DTO for Dapper mapping from SQLite (SQLite stores booleans as Int64)
internal record UserRecordDto(
    string id,
    string email,
    string normalized_email,
    string password_hash,
    string security_stamp,
    long permission_level,
    string? invited_by,
    long is_active,
    string? totp_secret,
    long totp_enabled,
    long created_at,
    long? last_login_at,
    long status,
    string? modified_by,
    long? modified_at,
    long email_verified,
    string? email_verification_token,
    long? email_verification_token_expires_at,
    string? password_reset_token,
    long? password_reset_token_expires_at,
    long? totp_setup_started_at
)
{
    // Map DTO to domain record
    public UserRecord ToUserRecord() => new UserRecord(
        Id: id,
        Email: email,
        NormalizedEmail: normalized_email,
        PasswordHash: password_hash,
        SecurityStamp: security_stamp,
        PermissionLevel: permission_level,
        InvitedBy: invited_by,
        IsActive: is_active != 0,
        TotpSecret: totp_secret,
        TotpEnabled: totp_enabled != 0,
        TotpSetupStartedAt: totp_setup_started_at,
        CreatedAt: created_at,
        LastLoginAt: last_login_at,
        Status: (UserStatus)status,
        ModifiedBy: modified_by,
        ModifiedAt: modified_at,
        EmailVerified: email_verified != 0,
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
    long PermissionLevel,
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
internal record RecoveryCodeRecordDto(
    long Id,
    string UserId,
    string CodeHash,
    long? UsedAt
)
{
    public RecoveryCodeRecord ToRecoveryCodeRecord() => new RecoveryCodeRecord(
        Id: Id,
        UserId: UserId,
        CodeHash: CodeHash,
        UsedAt: UsedAt
    );
}

public record RecoveryCodeRecord(
    long Id,
    string UserId,
    string CodeHash,
    long? UsedAt
);

// DTO for InviteRecord
internal record InviteRecordDto(
    string token,
    string created_by,
    long created_at,
    long expires_at,
    string? used_by,
    long permission_level,
    long status,
    long? modified_at
)
{
    public InviteRecord ToInviteRecord() => new InviteRecord(
        Token: token,
        CreatedBy: created_by,
        CreatedAt: created_at,
        ExpiresAt: expires_at,
        UsedBy: used_by,
        PermissionLevel: (int)permission_level,
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
internal record InviteWithCreatorDto(
    string token,
    string created_by,
    long created_at,
    long expires_at,
    string? used_by,
    long permission_level,
    long status,
    long? modified_at,
    string? creator_email
)
{
    public InviteWithCreator ToInviteWithCreator() => new InviteWithCreator(
        Invite: new InviteRecord(
            Token: token,
            CreatedBy: created_by,
            CreatedAt: created_at,
            ExpiresAt: expires_at,
            UsedBy: used_by,
            PermissionLevel: (int)permission_level,
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
internal record AuditLogRecordDto(
    long id,
    long event_type,
    long timestamp,
    string? actor_user_id,
    string? target_user_id,
    string? value
)
{
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
