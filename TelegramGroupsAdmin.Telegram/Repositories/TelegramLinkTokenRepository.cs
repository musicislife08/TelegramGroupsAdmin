using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Uses DbContextFactory to avoid concurrency issues
/// </summary>
public class TelegramLinkTokenRepository : ITelegramLinkTokenRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TelegramLinkTokenRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task InsertAsync(TelegramLinkTokenRecord token)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = token.ToDataModel();
        context.TelegramLinkTokens.Add(entity);
        await context.SaveChangesAsync();
    }

    public async Task<TelegramLinkTokenRecord?> GetByTokenAsync(string token)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.TelegramLinkTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(tlt => tlt.Token == token);

        return entity?.ToUiModel();
    }

    public async Task MarkAsUsedAsync(string token, long telegramId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.TelegramLinkTokens.FirstOrDefaultAsync(tlt => tlt.Token == token);
        if (entity == null) return;

        entity.UsedAt = DateTimeOffset.UtcNow;
        entity.UsedByTelegramId = telegramId;
        await context.SaveChangesAsync();
    }

    public async Task DeleteExpiredTokensAsync(DateTimeOffset beforeTimestamp)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var expiredTokens = await context.TelegramLinkTokens
            .Where(tlt => tlt.ExpiresAt < beforeTimestamp)
            .ToListAsync();

        if (expiredTokens.Count > 0)
        {
            context.TelegramLinkTokens.RemoveRange(expiredTokens);
            await context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<TelegramLinkTokenRecord>> GetActiveTokensForUserAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;

        var entities = await context.TelegramLinkTokens
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
        await using var context = await _contextFactory.CreateDbContextAsync();
        var tokensToRevoke = await context.TelegramLinkTokens
            .Where(tlt => tlt.UserId == userId && tlt.UsedAt == null)
            .ToListAsync();

        if (tokensToRevoke.Count > 0)
        {
            context.TelegramLinkTokens.RemoveRange(tokensToRevoke);
            await context.SaveChangesAsync();
        }
    }
}
