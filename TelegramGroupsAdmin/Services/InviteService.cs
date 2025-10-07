using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Data.Repositories;

namespace TelegramGroupsAdmin.Services;

public class InviteService : IInviteService
{
    private readonly InviteRepository _inviteRepository;
    private readonly ILogger<InviteService> _logger;

    public InviteService(InviteRepository inviteRepository, ILogger<InviteService> logger)
    {
        _inviteRepository = inviteRepository;
        _logger = logger;
    }

    public async Task<InviteResult> CreateInviteAsync(string createdBy, int expirationDays = 7, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = createdAt.AddDays(expirationDays);

        var invite = new InviteRecord(
            Token: token,
            CreatedBy: createdBy,
            CreatedAt: createdAt.ToUnixTimeSeconds(),
            ExpiresAt: expiresAt.ToUnixTimeSeconds(),
            UsedBy: null,
            UsedAt: null
        );

        await _inviteRepository.CreateAsync(invite, ct);

        _logger.LogInformation("Created invite {Token} by user {UserId}, expires at {ExpiresAt}",
            token, createdBy, expiresAt);

        // Generate URL (will be configured based on environment)
        var url = $"/register?invite={token}";

        return new InviteResult(token, url, expiresAt);
    }

    public async Task<InviteRecord?> GetInviteAsync(string token, CancellationToken ct = default)
    {
        return await _inviteRepository.GetByTokenAsync(token, ct);
    }

    public async Task<List<InviteListItem>> GetUserInvitesAsync(string userId, CancellationToken ct = default)
    {
        var invites = await _inviteRepository.GetByCreatorAsync(userId, ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return invites.Select(i => new InviteListItem(
            Token: i.Token,
            CreatedAt: DateTimeOffset.FromUnixTimeSeconds(i.CreatedAt),
            ExpiresAt: DateTimeOffset.FromUnixTimeSeconds(i.ExpiresAt),
            UsedBy: i.UsedBy,
            UsedAt: i.UsedAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(i.UsedAt.Value) : null,
            IsExpired: i.ExpiresAt < now,
            IsUsed: i.UsedAt.HasValue
        )).ToList();
    }
}
