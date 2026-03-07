using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Core.Repositories;

/// <summary>
/// Repository for managing browser push notification subscriptions
/// </summary>
public class PushSubscriptionsRepository(IDbContextFactory<AppDbContext> contextFactory)
    : IPushSubscriptionsRepository
{
    public async Task<IReadOnlyList<PushSubscription>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var dtos = await context.PushSubscriptions
            .AsNoTracking()
            .Where(ps => ps.UserId == userId)
            .OrderByDescending(ps => ps.CreatedAt)
            .ToListAsync(cancellationToken);

        return dtos.Select(d => d.ToModel()).ToList();
    }

    public async Task<PushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var dto = await context.PushSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(ps => ps.Endpoint == endpoint, cancellationToken);

        return dto?.ToModel();
    }

    public async Task<PushSubscription> UpsertAsync(PushSubscription subscription, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Check for existing subscription by user + endpoint
        var existingId = await context.PushSubscriptions
            .Where(ps => ps.UserId == subscription.UserId && ps.Endpoint == subscription.Endpoint)
            .Select(ps => ps.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingId > 0)
        {
            // Update existing subscription (keys may have changed) using bulk update
            await context.PushSubscriptions
                .Where(ps => ps.Id == existingId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(ps => ps.P256dh, subscription.P256dh)
                    .SetProperty(ps => ps.Auth, subscription.Auth)
                    .SetProperty(ps => ps.UserAgent, subscription.UserAgent), cancellationToken);

            // Fetch and return the updated record
            var updated = await context.PushSubscriptions
                .AsNoTracking()
                .FirstAsync(ps => ps.Id == existingId, cancellationToken);
            return updated.ToModel();
        }

        // Create new subscription
        var dto = subscription.ToDto() with { Id = 0, CreatedAt = DateTimeOffset.UtcNow };
        context.PushSubscriptions.Add(dto);
        await context.SaveChangesAsync(cancellationToken);

        return dto.ToModel();
    }

    public async Task<bool> DeleteByEndpointAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await context.PushSubscriptions
            .Where(ps => ps.Endpoint == endpoint)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    public async Task<int> DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.PushSubscriptions
            .Where(ps => ps.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
