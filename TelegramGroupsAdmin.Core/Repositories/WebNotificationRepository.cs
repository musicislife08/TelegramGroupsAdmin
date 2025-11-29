using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Core.Repositories;

public class WebNotificationRepository(IDbContextFactory<AppDbContext> contextFactory)
    : IWebNotificationRepository
{
    public async Task<WebNotification> CreateAsync(WebNotification notification, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var dto = notification.ToDto();
        context.WebNotifications.Add(dto);
        await context.SaveChangesAsync(ct);

        return dto.ToModel();
    }

    public async Task<IReadOnlyList<WebNotification>> GetRecentAsync(
        string userId,
        int limit = 20,
        int offset = 0,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var notifications = await context.WebNotifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        return notifications.Select(n => n.ToModel()).ToList();
    }

    public async Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.WebNotifications
            .CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }

    public async Task MarkAsReadAsync(long notificationId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        await context.WebNotifications
            .Where(n => n.Id == notificationId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task MarkAllAsReadAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        await context.WebNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task<int> DeleteOldReadNotificationsAsync(TimeSpan retention, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var cutoff = DateTimeOffset.UtcNow - retention;

        return await context.WebNotifications
            .Where(n => n.IsRead && n.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
