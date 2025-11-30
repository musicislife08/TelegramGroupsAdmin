using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories;

/// <summary>
/// Repository for managing browser push notification subscriptions
/// </summary>
public class PushSubscriptionsRepository(IDbContextFactory<AppDbContext> contextFactory)
    : IPushSubscriptionsRepository
{
    public async Task<IReadOnlyList<PushSubscription>> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dtos = await context.PushSubscriptions
            .AsNoTracking()
            .Where(ps => ps.UserId == userId)
            .OrderByDescending(ps => ps.CreatedAt)
            .ToListAsync(ct);

        return dtos.Select(d => d.ToModel()).ToList();
    }

    public async Task<PushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dto = await context.PushSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(ps => ps.Endpoint == endpoint, ct);

        return dto?.ToModel();
    }

    public async Task<PushSubscription> UpsertAsync(PushSubscription subscription, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // Check for existing subscription by user + endpoint
        var existingId = await context.PushSubscriptions
            .Where(ps => ps.UserId == subscription.UserId && ps.Endpoint == subscription.Endpoint)
            .Select(ps => ps.Id)
            .FirstOrDefaultAsync(ct);

        if (existingId > 0)
        {
            // Update existing subscription (keys may have changed) using bulk update
            await context.PushSubscriptions
                .Where(ps => ps.Id == existingId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(ps => ps.P256dh, subscription.P256dh)
                    .SetProperty(ps => ps.Auth, subscription.Auth)
                    .SetProperty(ps => ps.UserAgent, subscription.UserAgent), ct);

            // Fetch and return the updated record
            var updated = await context.PushSubscriptions
                .AsNoTracking()
                .FirstAsync(ps => ps.Id == existingId, ct);
            return updated.ToModel();
        }

        // Create new subscription
        var dto = subscription.ToDto() with { Id = 0, CreatedAt = DateTimeOffset.UtcNow };
        context.PushSubscriptions.Add(dto);
        await context.SaveChangesAsync(ct);

        return dto.ToModel();
    }

    public async Task<bool> DeleteByEndpointAsync(string endpoint, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var deleted = await context.PushSubscriptions
            .Where(ps => ps.Endpoint == endpoint)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }

    public async Task<int> DeleteByUserIdAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.PushSubscriptions
            .Where(ps => ps.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }
}
