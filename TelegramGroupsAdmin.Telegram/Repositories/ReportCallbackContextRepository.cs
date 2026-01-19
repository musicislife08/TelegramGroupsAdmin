using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Mappings;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class ReportCallbackContextRepository : IReportCallbackContextRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ReportCallbackContextRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<long> CreateAsync(
        ReportCallbackContext context,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = context.ToDto();
        dbContext.ReportCallbackContexts.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task<ReportCallbackContext?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.ReportCallbackContexts
            .FirstOrDefaultAsync(rcc => rcc.Id == id, cancellationToken);

        return entity?.ToModel();
    }

    public async Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await dbContext.ReportCallbackContexts
            .Where(rcc => rcc.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteByReportIdAsync(
        long reportId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await dbContext.ReportCallbackContexts
            .Where(rcc => rcc.ReportId == reportId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoff = DateTimeOffset.UtcNow - maxAge;

        return await dbContext.ReportCallbackContexts
            .Where(rcc => rcc.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
