namespace TelegramGroupsAdmin.Core.Models;

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
