using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Uses DbContextFactory to avoid concurrency issues
/// </summary>
public class TelegramUserMappingRepository : ITelegramUserMappingRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TelegramUserMappingRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<TelegramUserMappingRecord>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.TelegramUserMappings
            .AsNoTracking()
            .Where(tum => tum.UserId == userId && tum.IsActive)
            .OrderByDescending(tum => tum.LinkedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel());
    }

    public async Task<TelegramUserMappingRecord?> GetByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUserMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(tum => tum.TelegramId == telegramId && tum.IsActive, cancellationToken);

        return entity?.ToModel();
    }

    public async Task<string?> GetUserIdByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUserMappings
            .AsNoTracking()
            .Where(tum => tum.TelegramId == telegramId && tum.IsActive)
            .Select(tum => tum.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        return entity;
    }

    public async Task<long> InsertAsync(TelegramUserMappingRecord mapping, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = mapping.ToDto();
        context.TelegramUserMappings.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<bool> DeactivateAsync(long mappingId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUserMappings.FirstOrDefaultAsync(tum => tum.Id == mappingId, cancellationToken);
        if (entity == null)
            return false;

        entity.IsActive = false;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> IsTelegramIdLinkedAsync(long telegramId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TelegramUserMappings
            .AsNoTracking()
            .AnyAsync(tum => tum.TelegramId == telegramId && tum.IsActive, cancellationToken);
    }

    public async Task<int?> GetPermissionLevelByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Single query with JOIN - returns null if no mapping or user found
        var permissionLevel = await context.TelegramUserMappings
            .AsNoTracking()
            .Where(tum => tum.TelegramId == telegramId && tum.IsActive)
            .Join(
                context.Users,
                tum => tum.UserId,
                user => user.Id,
                (tum, user) => (int?)user.PermissionLevel)
            .FirstOrDefaultAsync(cancellationToken);

        return permissionLevel;
    }
}
