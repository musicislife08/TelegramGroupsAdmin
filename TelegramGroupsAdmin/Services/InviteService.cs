using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Data.Repositories;

namespace TelegramGroupsAdmin.Services;

public class InviteService : IInviteService
{
    private readonly InviteRepository _inviteRepository;
    private readonly IAuditService _auditService;
    private readonly ILogger<InviteService> _logger;
    private readonly AppOptions _appOptions;

    public InviteService(
        InviteRepository inviteRepository,
        IAuditService auditService,
        IOptions<AppOptions> appOptions,
        ILogger<InviteService> logger)
    {
        _inviteRepository = inviteRepository;
        _auditService = auditService;
        _appOptions = appOptions.Value;
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
            PermissionLevel: 0, // Default to ReadOnly
            Status: Data.Models.InviteStatus.Pending,
            ModifiedAt: null
        );

        await _inviteRepository.CreateAsync(invite, ct);

        _logger.LogInformation("Created invite {Token} by user {UserId}, expires at {ExpiresAt}",
            token, createdBy, expiresAt);

        // Generate full URL using configured base URL
        var url = $"{_appOptions.BaseUrl}/register?invite={Uri.EscapeDataString(token)}";

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
            UsedAt: i.ModifiedAt.HasValue && i.Status == Data.Models.InviteStatus.Used
                ? DateTimeOffset.FromUnixTimeSeconds(i.ModifiedAt.Value)
                : null,
            IsExpired: i.ExpiresAt < now && i.Status == Data.Models.InviteStatus.Pending,
            IsUsed: i.Status == Data.Models.InviteStatus.Used
        )).ToList();
    }

    public async Task<string> CreateInviteWithPermissionAsync(string createdBy, int permissionLevel, int validDays = 7, CancellationToken ct = default)
    {
        var token = await _inviteRepository.CreateAsync(createdBy, validDays, permissionLevel, ct);

        var permissionName = permissionLevel switch
        {
            0 => "ReadOnly",
            1 => "Admin",
            2 => "Owner",
            _ => permissionLevel.ToString()
        };

        // Audit log
        await _auditService.LogEventAsync(
            AuditEventType.UserInviteCreated,
            actorUserId: createdBy,
            targetUserId: null,
            value: $"Invite expires in {validDays} days, permission: {permissionName}",
            ct: ct);

        return token;
    }

    public async Task<List<InviteWithCreator>> GetAllInvitesAsync(string? filter = "pending", CancellationToken ct = default)
    {
        // Convert UI string filter to enum for type-safe repository access
        var enumFilter = filter?.ToLower() switch
        {
            "pending" => InviteFilter.Pending,
            "used" => InviteFilter.Used,
            "revoked" => InviteFilter.Revoked,
            "all" => InviteFilter.All,
            _ => InviteFilter.Pending
        };

        return await _inviteRepository.GetAllWithCreatorEmailAsync(enumFilter, ct);
    }

    public async Task<bool> RevokeInviteAsync(string token, string revokedBy, CancellationToken ct = default)
    {
        // Get invite details before revocation for audit logging
        var invite = await _inviteRepository.GetByTokenAsync(token, ct);

        var success = await _inviteRepository.RevokeAsync(token, ct);

        if (success)
        {
            // Audit log
            await _auditService.LogEventAsync(
                AuditEventType.UserInviteRevoked,
                actorUserId: revokedBy,
                targetUserId: null,
                value: $"Revoked invite (permission: {invite?.PermissionLevel ?? 0})",
                ct: ct);
        }

        return success;
    }
}
