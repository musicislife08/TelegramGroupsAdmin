using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class UsernameHistoryRepository(IDbContextFactory<AppDbContext> contextFactory) : IUsernameHistoryRepository
{
    public async Task<List<UsernameHistoryRecord>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var dtos = await context.UsernameHistory
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.RecordedAt)
            .ToListAsync(cancellationToken);

        return dtos.Select(h => h.ToModel()).ToList();
    }

    public async Task<bool> HasChangeSinceAsync(long userId, DateTimeOffset? since, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var history = context.UsernameHistory.AsNoTracking().Where(h => h.UserId == userId);
        if (since is { } after)
        {
            // Npgsql writes timestamptz parameters only from offset-zero values.
            var afterUtc = after.ToUniversalTime();
            history = history.Where(h => h.RecordedAt > afterUtc);
        }
        return await history.AnyAsync(cancellationToken);
    }
}
