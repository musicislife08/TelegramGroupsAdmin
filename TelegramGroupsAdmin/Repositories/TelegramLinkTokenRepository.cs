using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class TelegramLinkTokenRepository : ITelegramLinkTokenRepository
{
    private readonly AppDbContext _context;

    public TelegramLinkTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task InsertAsync(TelegramLinkTokenRecord token)
    {
        var entity = token.ToDataModel();
        _context.TelegramLinkTokens.Add(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<TelegramLinkTokenRecord?> GetByTokenAsync(string token)
    {
        var entity = await _context.TelegramLinkTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(tlt => tlt.Token == token);

        return entity?.ToUiModel();
    }

    public async Task MarkAsUsedAsync(string token, long telegramId)
    {
        var entity = await _context.TelegramLinkTokens.FirstOrDefaultAsync(tlt => tlt.Token == token);
        if (entity == null) return;

        entity.UsedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        entity.UsedByTelegramId = telegramId;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteExpiredTokensAsync(long beforeTimestamp)
    {
        var expiredTokens = await _context.TelegramLinkTokens
            .Where(tlt => tlt.ExpiresAt < beforeTimestamp)
            .ToListAsync();

        if (expiredTokens.Count > 0)
        {
            _context.TelegramLinkTokens.RemoveRange(expiredTokens);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<TelegramLinkTokenRecord>> GetActiveTokensForUserAsync(string userId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var entities = await _context.TelegramLinkTokens
            .AsNoTracking()
            .Where(tlt => tlt.UserId == userId
                && tlt.UsedAt == null
                && tlt.ExpiresAt > now)
            .OrderByDescending(tlt => tlt.CreatedAt)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel());
    }

    public async Task RevokeUnusedTokensForUserAsync(string userId)
    {
        var tokensToRevoke = await _context.TelegramLinkTokens
            .Where(tlt => tlt.UserId == userId && tlt.UsedAt == null)
            .ToListAsync();

        if (tokensToRevoke.Count > 0)
        {
            _context.TelegramLinkTokens.RemoveRange(tokensToRevoke);
            await _context.SaveChangesAsync();
        }
    }
}
