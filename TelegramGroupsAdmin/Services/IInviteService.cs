using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Services;

public interface IInviteService
{
    Task<InviteResult> CreateInviteAsync(string createdBy, int expirationDays = 7, CancellationToken ct = default);
    Task<InviteRecord?> GetInviteAsync(string token, CancellationToken ct = default);
    Task<List<InviteListItem>> GetUserInvitesAsync(string userId, CancellationToken ct = default);
}

public record InviteResult(
    string Token,
    string Url,
    DateTimeOffset ExpiresAt
);

public record InviteListItem(
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? UsedBy,
    DateTimeOffset? UsedAt,
    bool IsExpired,
    bool IsUsed
);
