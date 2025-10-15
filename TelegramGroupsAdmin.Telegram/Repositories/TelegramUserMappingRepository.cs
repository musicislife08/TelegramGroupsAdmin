using Microsoft.EntityFrameworkCore;
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

    public async Task<IEnumerable<TelegramUserMappingRecord>> GetByUserIdAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.TelegramUserMappings
            .AsNoTracking()
            .Where(tum => tum.UserId == userId && tum.IsActive)
            .OrderByDescending(tum => tum.LinkedAt)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel());
    }

    public async Task<TelegramUserMappingRecord?> GetByTelegramIdAsync(long telegramId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.TelegramUserMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(tum => tum.TelegramId == telegramId && tum.IsActive);

        return entity?.ToUiModel();
    }

    public async Task<string?> GetUserIdByTelegramIdAsync(long telegramId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.TelegramUserMappings
            .AsNoTracking()
            .Where(tum => tum.TelegramId == telegramId && tum.IsActive)
            .Select(tum => tum.UserId)
            .FirstOrDefaultAsync();

        return entity;
    }

    public async Task<long> InsertAsync(TelegramUserMappingRecord mapping)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = mapping.ToDataModel();
        context.TelegramUserMappings.Add(entity);
        await context.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<bool> DeactivateAsync(long mappingId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.TelegramUserMappings.FirstOrDefaultAsync(tum => tum.Id == mappingId);
        if (entity == null)
            return false;

        entity.IsActive = false;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsTelegramIdLinkedAsync(long telegramId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.TelegramUserMappings
            .AsNoTracking()
            .AnyAsync(tum => tum.TelegramId == telegramId && tum.IsActive);
    }
}
