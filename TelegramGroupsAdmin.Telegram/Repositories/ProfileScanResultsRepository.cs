using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IProfileScanResultsRepository"/>.
/// </summary>
public class ProfileScanResultsRepository(IDbContextFactory<AppDbContext> contextFactory)
    : IProfileScanResultsRepository
{
    public async Task<long> InsertAsync(ProfileScanResultRecord record, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var dto = record.ToDto();
        context.ProfileScanResults.Add(dto);
        await context.SaveChangesAsync(cancellationToken);
        return dto.Id;
    }

    public async Task<List<ProfileScanResultRecord>> GetByUserIdAsync(long userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var results = await context.ProfileScanResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ScannedAt)
            .ToListAsync(cancellationToken);

        return results.Select(r => r.ToModel()).ToList();
    }

    public async Task<ProfileScanResultRecord?> GetLatestByUserIdAsync(long userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var dto = await context.ProfileScanResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ScannedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return dto?.ToModel();
    }
}
