using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class PendingNotificationsRepository : IPendingNotificationsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private const int DefaultExpirationDays = 30;

    public PendingNotificationsRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<PendingNotificationModel> AddPendingNotificationAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var record = new PendingNotificationRecordDto
        {
            TelegramUserId = telegramUserId,
            NotificationType = notificationType,
            MessageText = messageText,
            CreatedAt = now,
            RetryCount = 0,
            ExpiresAt = expiresAt ?? now.AddDays(DefaultExpirationDays)
        };

        context.PendingNotifications.Add(record);
        await context.SaveChangesAsync(cancellationToken);

        return record.ToModel();
    }

    public async Task<List<PendingNotificationModel>> GetPendingNotificationsForUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await context.PendingNotifications
            .Where(pn => pn.TelegramUserId == telegramUserId)
            .OrderBy(pn => pn.CreatedAt)
            .ToListAsync(cancellationToken);

        return records.Select(r => r.ToModel()).ToList();
    }

    public async Task DeletePendingNotificationAsync(
        long notificationId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.PendingNotifications
            .FirstOrDefaultAsync(pn => pn.Id == notificationId, cancellationToken);

        if (record != null)
        {
            context.PendingNotifications.Remove(record);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task IncrementRetryCountAsync(
        long notificationId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.PendingNotifications
            .FirstOrDefaultAsync(pn => pn.Id == notificationId, cancellationToken);

        if (record != null)
        {
            record.RetryCount++;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteAllPendingNotificationsForUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await context.PendingNotifications
            .Where(pn => pn.TelegramUserId == telegramUserId)
            .ToListAsync(cancellationToken);

        if (records.Any())
        {
            context.PendingNotifications.RemoveRange(records);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> DeleteExpiredNotificationsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var expiredRecords = await context.PendingNotifications
            .Where(pn => pn.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (expiredRecords.Any())
        {
            context.PendingNotifications.RemoveRange(expiredRecords);
            await context.SaveChangesAsync(cancellationToken);
        }

        return expiredRecords.Count;
    }

    public async Task<int> GetPendingNotificationCountAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.PendingNotifications
            .Where(pn => pn.TelegramUserId == telegramUserId)
            .CountAsync(cancellationToken);
    }
}
