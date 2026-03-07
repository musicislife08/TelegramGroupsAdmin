namespace TelegramGroupsAdmin.Services;

public interface IInviteService
{
    Task<InviteResult> CreateInviteAsync(string createdBy, int expirationDays = 7, CancellationToken cancellationToken = default);
    Task<string> CreateInviteWithPermissionAsync(string createdBy, int permissionLevel, int creatorPermissionLevel, int validDays = 7, CancellationToken cancellationToken = default);
    Task<InviteRecord?> GetInviteAsync(string token, CancellationToken cancellationToken = default);
    Task<List<InviteListItem>> GetUserInvitesAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<InviteWithCreator>> GetAllInvitesAsync(string? filter = "pending", CancellationToken cancellationToken = default);
    Task<bool> RevokeInviteAsync(string token, string revokedBy, CancellationToken cancellationToken = default);
}
