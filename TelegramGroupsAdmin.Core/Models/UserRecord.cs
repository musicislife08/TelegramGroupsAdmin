namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// User record for UI display.
/// Identity fields (Id, Email, PermissionLevel) are embedded in WebUser.
/// </summary>
public record UserRecord(
    WebUserIdentity WebUser,
    string NormalizedEmail,
    string PasswordHash,
    string SecurityStamp,
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
    DateTimeOffset? PasswordResetTokenExpiresAt,
    int FailedLoginAttempts,
    DateTimeOffset? LockedUntil
)
{
    public bool CanLogin => Status == UserStatus.Active && EmailVerified && !IsLocked;
    public bool IsPending => Status == UserStatus.Pending;
    public int PermissionLevelInt => (int)WebUser.PermissionLevel;
    public bool IsLocked => LockedUntil.HasValue && LockedUntil.Value > DateTimeOffset.UtcNow;
}
