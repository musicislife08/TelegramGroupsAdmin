using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Core.Repositories;

public class WebNotificationRepository(IDbContextFactory<AppDbContext> contextFactory)
    : IWebNotificationRepository
{
    public async Task<WebNotification> CreateAsync(WebNotification notification, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var dto = notification.ToDto();
        context.WebNotifications.Add(dto);
        await context.SaveChangesAsync(cancellationToken);

        return dto.ToModel();
    }

    public async Task<IReadOnlyList<WebNotification>> GetRecentAsync(
        string userId,
        int limit = QueryConstants.DefaultWebNotificationLimit,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var notifications = await context.WebNotifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return notifications.Select(n => n.ToModel()).ToList();
    }

    public async Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.WebNotifications
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
    }

    public async Task MarkAsReadAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.WebNotifications
            .Where(n => n.Id == notificationId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.WebNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task<int> DeleteOldReadNotificationsAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoff = DateTimeOffset.UtcNow - retention;

        return await context.WebNotifications
            .Where(n => n.IsRead && n.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.WebNotifications
            .Where(n => n.Id == notificationId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.WebNotifications
            .Where(n => n.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
