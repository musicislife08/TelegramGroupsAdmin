using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Mappings;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class ReviewCallbackContextRepository : IReviewCallbackContextRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ReviewCallbackContextRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<long> CreateAsync(
        ReviewCallbackContext context,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = context.ToDto();
        dbContext.ReviewCallbackContexts.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task<ReviewCallbackContext?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.ReviewCallbackContexts
            .FirstOrDefaultAsync(rcc => rcc.Id == id, cancellationToken);

        return entity?.ToModel();
    }

    public async Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await dbContext.ReviewCallbackContexts
            .Where(rcc => rcc.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteByReviewIdAsync(
        long reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await dbContext.ReviewCallbackContexts
            .Where(rcc => rcc.ReviewId == reviewId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoff = DateTimeOffset.UtcNow - maxAge;

        return await dbContext.ReviewCallbackContexts
            .Where(rcc => rcc.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
