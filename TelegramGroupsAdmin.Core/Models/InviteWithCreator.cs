namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Invite with creator information for UI display
/// </summary>
public record InviteWithCreator(
    string Token,
    string CreatedBy,
    string CreatedByEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? UsedBy,
    string? UsedByEmail,
    PermissionLevel PermissionLevel,
    InviteStatus Status,
    DateTimeOffset? ModifiedAt
)
{
    public bool IsExpired => ExpiresAt < DateTimeOffset.UtcNow;
    public bool IsValid => Status == InviteStatus.Pending && !IsExpired;
}
