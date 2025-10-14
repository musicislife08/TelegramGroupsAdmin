using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

public class InviteRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<InviteRepository> _logger;

    public InviteRepository(AppDbContext context, ILogger<InviteRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UiModels.InviteRecord?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        var entity = await _context.Invites
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Token == token, ct);

        return entity?.ToUiModel();
    }

    public async Task CreateAsync(UiModels.InviteRecord invite, CancellationToken ct = default)
    {
        var entity = invite.ToDataModel();
        _context.Invites.Add(entity);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created invite {Token} by user {CreatedBy}, expires at {ExpiresAt}, permission level {PermissionLevel}",
            invite.Token, invite.CreatedBy, DateTimeOffset.FromUnixTimeSeconds(invite.ExpiresAt), invite.PermissionLevel);
    }

    public async Task<string> CreateAsync(string createdBy, int validDays = 7, int permissionLevel = 0, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiresAt = now + (validDays * 24 * 3600);

        var entity = new DataModels.InviteRecord
        {
            Token = token,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            PermissionLevel = (DataModels.PermissionLevel)permissionLevel,
            Status = DataModels.InviteStatus.Pending,
            ModifiedAt = null,
            UsedBy = null
        };

        _context.Invites.Add(entity);
        await _context.SaveChangesAsync(ct);

        var permissionName = permissionLevel switch
        {
            0 => "ReadOnly",
            1 => "Admin",
            2 => "Owner",
            _ => permissionLevel.ToString()
        };

        _logger.LogInformation("Created invite {Token} by user {CreatedBy}, expires at {ExpiresAt}, permission level {PermissionLevel}",
            token, createdBy, DateTimeOffset.FromUnixTimeSeconds(expiresAt), permissionName);

        return token;
    }

    public async Task MarkAsUsedAsync(string token, string usedBy)
    {
        var entity = await _context.Invites.FirstOrDefaultAsync(i => i.Token == token);
        if (entity == null) return;

        entity.UsedBy = usedBy;
        entity.Status = DataModels.InviteStatus.Used;
        entity.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await _context.SaveChangesAsync();

        _logger.LogInformation("Invite {Token} used by user {UsedBy}", token, usedBy);
    }

    public async Task<List<UiModels.InviteRecord>> GetByCreatorAsync(string createdBy, CancellationToken ct = default)
    {
        var entities = await _context.Invites
            .AsNoTracking()
            .Where(i => i.CreatedBy == createdBy)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<int> CleanupExpiredAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var expiredInvites = await _context.Invites
            .Where(i => i.ExpiresAt <= now && i.Status == DataModels.InviteStatus.Pending)
            .ToListAsync();

        if (expiredInvites.Count > 0)
        {
            _context.Invites.RemoveRange(expiredInvites);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Cleaned up {Count} expired invites", expiredInvites.Count);
        }

        return expiredInvites.Count;
    }

    public async Task<List<UiModels.InviteRecord>> GetAllAsync(DataModels.InviteFilter filter = DataModels.InviteFilter.Pending, CancellationToken ct = default)
    {
        var query = _context.Invites.AsNoTracking();

        if (filter != DataModels.InviteFilter.All)
        {
            var status = (DataModels.InviteStatus)(int)filter;
            query = query.Where(i => i.Status == status);
        }

        var entities = await query.OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.InviteWithCreator>> GetAllWithCreatorEmailAsync(DataModels.InviteFilter filter = DataModels.InviteFilter.Pending, CancellationToken ct = default)
    {
        var query = _context.Invites
            .AsNoTracking()
            .Join(_context.Users,
                invite => invite.CreatedBy,
                user => user.Id,
                (invite, user) => new { Invite = invite, CreatorEmail = user.Email })
            .GroupJoin(_context.Users,
                x => x.Invite.UsedBy,
                user => user.Id,
                (x, usedByUsers) => new { x.Invite, x.CreatorEmail, UsedByEmail = usedByUsers.Select(u => u.Email).FirstOrDefault() });

        if (filter != DataModels.InviteFilter.All)
        {
            var status = (DataModels.InviteStatus)(int)filter;
            query = query.Where(x => x.Invite.Status == status);
        }

        var results = await query.OrderByDescending(x => x.Invite.CreatedAt).ToListAsync(ct);

        return results.Select(r => new UiModels.InviteWithCreator(
            Token: r.Invite.Token,
            CreatedBy: r.Invite.CreatedBy,
            CreatedByEmail: r.CreatorEmail,
            CreatedAt: r.Invite.CreatedAt,
            ExpiresAt: r.Invite.ExpiresAt,
            UsedBy: r.Invite.UsedBy,
            UsedByEmail: r.UsedByEmail,
            PermissionLevel: (UiModels.PermissionLevel)r.Invite.PermissionLevel,
            Status: (UiModels.InviteStatus)r.Invite.Status,
            ModifiedAt: r.Invite.ModifiedAt
        )).ToList();
    }

    public async Task<bool> RevokeAsync(string token, CancellationToken ct = default)
    {
        var entity = await _context.Invites
            .FirstOrDefaultAsync(i => i.Token == token && i.Status == DataModels.InviteStatus.Pending, ct);

        if (entity == null)
            return false;

        entity.Status = DataModels.InviteStatus.Revoked;
        entity.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Revoked invite {Token}", token);
        return true;
    }
}
