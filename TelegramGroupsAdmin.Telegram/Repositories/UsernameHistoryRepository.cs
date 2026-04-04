using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class UsernameHistoryRepository(IDbContextFactory<AppDbContext> contextFactory) : IUsernameHistoryRepository
{
    public async Task InsertAsync(long userId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.UsernameHistory.Add(new UsernameHistoryDto
        {
            UserId = userId,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            RecordedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);
    }

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
}
