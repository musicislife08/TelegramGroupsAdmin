using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Repositories;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Services;

public class InviteService : IInviteService
{
    private readonly IInviteRepository _inviteRepository;
    private readonly IAuditService _auditService;
    private readonly ILogger<InviteService> _logger;
    private readonly AppOptions _appOptions;

    public InviteService(
        IInviteRepository inviteRepository,
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
            CreatedAt: createdAt,
            ExpiresAt: expiresAt,
            UsedBy: null,
            PermissionLevel: 0, // Default to Admin
            Status: InviteStatus.Pending,
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
        var now = DateTimeOffset.UtcNow;

        return invites.Select(i => new InviteListItem(
            Token: i.Token,
            CreatedAt: i.CreatedAt,
            ExpiresAt: i.ExpiresAt,
            UsedBy: i.UsedBy,
            UsedAt: i is { ModifiedAt: not null, Status: InviteStatus.Used }
                ? i.ModifiedAt.Value
                : null,
            IsExpired: i.ExpiresAt < now && i.Status == InviteStatus.Pending,
            IsUsed: i.Status == InviteStatus.Used
        )).ToList();
    }

    public async Task<string> CreateInviteWithPermissionAsync(string createdBy, int permissionLevel, int creatorPermissionLevel, int validDays = 7, CancellationToken ct = default)
    {
        // Escalation prevention: Users cannot create invites for permission levels above their own
        if (permissionLevel > creatorPermissionLevel)
        {
            var creatorPermissionName = creatorPermissionLevel switch
            {
                0 => "Admin",
                1 => "GlobalAdmin",
                2 => "Owner",
                _ => creatorPermissionLevel.ToString()
            };

            var requestedPermissionName = permissionLevel switch
            {
                0 => "Admin",
                1 => "GlobalAdmin",
                2 => "Owner",
                _ => permissionLevel.ToString()
            };

            throw new UnauthorizedAccessException(
                $"Cannot create invite with permission level {requestedPermissionName} (your level: {creatorPermissionName})");
        }

        var token = await _inviteRepository.CreateAsync(createdBy, validDays, permissionLevel, ct);

        var permissionName = permissionLevel switch
        {
            0 => "Admin",
            1 => "GlobalAdmin",
            2 => "Owner",
            _ => permissionLevel.ToString()
        };

        // Audit log
        await _auditService.LogEventAsync(
            AuditEventType.UserInviteCreated,
            actor: Core.Models.Actor.FromWebUser(createdBy),
            target: null,
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

        return await _inviteRepository.GetAllWithCreatorEmailAsync((DataModels.InviteFilter)enumFilter, ct);
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
                actor: Core.Models.Actor.FromWebUser(revokedBy),
                target: null,
                value: $"Revoked invite (permission: {invite?.PermissionLevel ?? 0})",
                ct: ct);
        }

        return success;
    }
}
