using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Mappings;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class InviteRepository : IInviteRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<InviteRepository> _logger;

    public InviteRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<InviteRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<InviteRecord?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Invites
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Token == token, cancellationToken);

        return entity?.ToModel();
    }

    public async Task CreateAsync(InviteRecord invite, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = invite.ToDto();
        context.Invites.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created invite {Token} by user {CreatedBy}, expires at {ExpiresAt}, permission level {PermissionLevel}",
            invite.Token, invite.CreatedBy, invite.ExpiresAt, invite.PermissionLevel);
    }

    public async Task<string> CreateAsync(string createdBy, int validDays = 7, int permissionLevel = 0, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var token = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(validDays);

        var entity = new DataModels.InviteRecordDto
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

        context.Invites.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        var permissionName = permissionLevel switch
        {
            0 => "Admin",
            1 => "GlobalAdmin",
            2 => "Owner",
            _ => permissionLevel.ToString()
        };

        _logger.LogInformation("Created invite {Token} by user {CreatedBy}, expires at {ExpiresAt}, permission level {PermissionLevel}",
            token, createdBy, expiresAt, permissionName);

        return token;
    }

    public async Task MarkAsUsedAsync(string token, string usedBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Invites.FirstOrDefaultAsync(i => i.Token == token, cancellationToken);
        if (entity == null) return;

        entity.UsedBy = usedBy;
        entity.Status = DataModels.InviteStatus.Used;
        entity.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Invite {Token} used by user {UsedBy}", token, usedBy);
    }

    public async Task<List<InviteRecord>> GetByCreatorAsync(string createdBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Invites
            .AsNoTracking()
            .Where(i => i.CreatedBy == createdBy)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var expiredInvites = await context.Invites
            .Where(i => i.ExpiresAt <= now && i.Status == DataModels.InviteStatus.Pending)
            .ToListAsync(cancellationToken);

        if (expiredInvites.Count > 0)
        {
            context.Invites.RemoveRange(expiredInvites);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Cleaned up {Count} expired invites", expiredInvites.Count);
        }

        return expiredInvites.Count;
    }

    public async Task<List<InviteRecord>> GetAllAsync(DataModels.InviteFilter filter = DataModels.InviteFilter.Pending, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Invites.AsNoTracking();

        if (filter != DataModels.InviteFilter.All)
        {
            var status = (DataModels.InviteStatus)(int)filter;
            query = query.Where(i => i.Status == status);
        }

        var entities = await query.OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken);
        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<InviteWithCreator>> GetAllWithCreatorEmailAsync(DataModels.InviteFilter filter = DataModels.InviteFilter.Pending, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Invites
            .AsNoTracking()
            .Join(context.Users,
                invite => invite.CreatedBy,
                user => user.Id,
                (invite, user) => new DataModels.InviteWithCreatorDto
                {
                    Invite = invite,
                    CreatorEmail = user.Email
                })
            .GroupJoin(context.Users,
                x => x.Invite.UsedBy,
                user => user.Id,
                (x, usedByUsers) => new DataModels.InviteWithCreatorDto
                {
                    Invite = x.Invite,
                    CreatorEmail = x.CreatorEmail,
                    UsedByEmail = usedByUsers.Select(u => u.Email).FirstOrDefault()
                });

        if (filter != DataModels.InviteFilter.All)
        {
            var status = (DataModels.InviteStatus)(int)filter;
            query = query.Where(x => x.Invite.Status == status);
        }

        var results = await query.OrderByDescending(x => x.Invite.CreatedAt).ToListAsync(cancellationToken);

        return results.Select(r => new InviteWithCreator(
            Token: r.Invite.Token,
            CreatedBy: r.Invite.CreatedBy,
            CreatedByEmail: r.CreatorEmail,
            CreatedAt: r.Invite.CreatedAt,
            ExpiresAt: r.Invite.ExpiresAt,
            UsedBy: r.Invite.UsedBy,
            UsedByEmail: r.UsedByEmail,
            PermissionLevel: (PermissionLevel)r.Invite.PermissionLevel,
            Status: (InviteStatus)r.Invite.Status,
            ModifiedAt: r.Invite.ModifiedAt
        )).ToList();
    }

    public async Task<bool> RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Invites
            .FirstOrDefaultAsync(i => i.Token == token && i.Status == DataModels.InviteStatus.Pending, cancellationToken);

        if (entity == null)
            return false;

        entity.Status = DataModels.InviteStatus.Revoked;
        entity.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Revoked invite {Token}", token);
        return true;
    }
}
