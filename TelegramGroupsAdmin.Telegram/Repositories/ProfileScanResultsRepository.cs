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
    public async Task<long> InsertAsync(ProfileScanResultRecord record, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dto = record.ToDto();
        context.ProfileScanResults.Add(dto);
        await context.SaveChangesAsync(ct);
        return dto.Id;
    }

    public async Task<List<ProfileScanResultRecord>> GetByUserIdAsync(long userId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var results = await context.ProfileScanResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ScannedAt)
            .ToListAsync(ct);

        return results.Select(r => r.ToModel()).ToList();
    }

    public async Task<ProfileScanResultRecord?> GetLatestByUserIdAsync(long userId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dto = await context.ProfileScanResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ScannedAt)
            .FirstOrDefaultAsync(ct);

        return dto?.ToModel();
    }
}
