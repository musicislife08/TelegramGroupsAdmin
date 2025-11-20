namespace TelegramGroupsAdmin.Telegram.Models;

using TelegramGroupsAdmin.Core.Models;

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
    DateTimeOffset? PasswordResetTokenExpiresAt,
    int FailedLoginAttempts,
    DateTimeOffset? LockedUntil
)
{
    public bool CanLogin => Status == UserStatus.Active && EmailVerified && !IsLocked;
    public bool IsPending => Status == UserStatus.Pending;
    public int PermissionLevelInt => (int)PermissionLevel;
    public bool IsLocked => LockedUntil.HasValue && LockedUntil.Value > DateTimeOffset.UtcNow;
}
