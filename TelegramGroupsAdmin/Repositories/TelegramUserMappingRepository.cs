using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class TelegramUserMappingRepository : ITelegramUserMappingRepository
{
    private readonly AppDbContext _context;

    public TelegramUserMappingRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<TelegramUserMappingRecord>> GetByUserIdAsync(string userId)
    {
        var entities = await _context.TelegramUserMappings
            .AsNoTracking()
            .Where(tum => tum.UserId == userId && tum.IsActive)
            .OrderByDescending(tum => tum.LinkedAt)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel());
    }

    public async Task<TelegramUserMappingRecord?> GetByTelegramIdAsync(long telegramId)
    {
        var entity = await _context.TelegramUserMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(tum => tum.TelegramId == telegramId && tum.IsActive);

        return entity?.ToUiModel();
    }

    public async Task<string?> GetUserIdByTelegramIdAsync(long telegramId)
    {
        var entity = await _context.TelegramUserMappings
            .AsNoTracking()
            .Where(tum => tum.TelegramId == telegramId && tum.IsActive)
            .Select(tum => tum.UserId)
            .FirstOrDefaultAsync();

        return entity;
    }

    public async Task<long> InsertAsync(TelegramUserMappingRecord mapping)
    {
        var entity = mapping.ToDataModel();
        _context.TelegramUserMappings.Add(entity);
        await _context.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<bool> DeactivateAsync(long mappingId)
    {
        var entity = await _context.TelegramUserMappings.FirstOrDefaultAsync(tum => tum.Id == mappingId);
        if (entity == null)
            return false;

        entity.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsTelegramIdLinkedAsync(long telegramId)
    {
        return await _context.TelegramUserMappings
            .AsNoTracking()
            .AnyAsync(tum => tum.TelegramId == telegramId && tum.IsActive);
    }
}
