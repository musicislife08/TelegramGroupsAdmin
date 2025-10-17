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

    public async Task InsertAsync(TelegramLinkTokenRecord token, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = token.ToDto();
        context.TelegramLinkTokens.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<TelegramLinkTokenRecord?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramLinkTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(tlt => tlt.Token == token, cancellationToken);

        return entity?.ToModel();
    }

    public async Task MarkAsUsedAsync(string token, long telegramId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramLinkTokens.FirstOrDefaultAsync(tlt => tlt.Token == token, cancellationToken);
        if (entity == null) return;

        entity.UsedAt = DateTimeOffset.UtcNow;
        entity.UsedByTelegramId = telegramId;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteExpiredTokensAsync(DateTimeOffset beforeTimestamp, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var expiredTokens = await context.TelegramLinkTokens
            .Where(tlt => tlt.ExpiresAt < beforeTimestamp)
            .ToListAsync(cancellationToken);

        if (expiredTokens.Count > 0)
        {
            context.TelegramLinkTokens.RemoveRange(expiredTokens);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<TelegramLinkTokenRecord>> GetActiveTokensForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var entities = await context.TelegramLinkTokens
            .AsNoTracking()
            .Where(tlt => tlt.UserId == userId
                && tlt.UsedAt == null
                && tlt.ExpiresAt > now)
            .OrderByDescending(tlt => tlt.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel());
    }

    public async Task RevokeUnusedTokensForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var tokensToRevoke = await context.TelegramLinkTokens
            .Where(tlt => tlt.UserId == userId && tlt.UsedAt == null)
            .ToListAsync(cancellationToken);

        if (tokensToRevoke.Count > 0)
        {
            context.TelegramLinkTokens.RemoveRange(tokensToRevoke);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
