namespace TelegramGroupsAdmin.Data.Models;

public record UserRecord(
    string Id,
    string Email,
    string NormalizedEmail,
    string PasswordHash,
    string SecurityStamp,
    int PermissionLevel,
    string? InvitedBy,
    bool IsActive,
    string? TotpSecret,
    bool TotpEnabled,
    long CreatedAt,
    long? LastLoginAt
);

public record RecoveryCodeRecord(
    long Id,
    string UserId,
    string CodeHash,
    long? UsedAt
);

public record InviteRecord(
    string Token,
    string CreatedBy,
    long CreatedAt,
    long ExpiresAt,
    string? UsedBy,
    long? UsedAt
);
