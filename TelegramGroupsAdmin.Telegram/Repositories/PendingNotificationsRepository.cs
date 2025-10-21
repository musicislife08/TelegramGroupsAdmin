using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class PendingNotificationsRepository : IPendingNotificationsRepository
{
    private readonly AppDbContext _context;
    private const int DefaultExpirationDays = 30;

    public PendingNotificationsRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PendingNotificationModel> AddPendingNotificationAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new PendingNotificationRecord
        {
            TelegramUserId = telegramUserId,
            NotificationType = notificationType,
            MessageText = messageText,
            CreatedAt = now,
            RetryCount = 0,
            ExpiresAt = expiresAt ?? now.AddDays(DefaultExpirationDays)
        };

        _context.PendingNotifications.Add(record);
        await _context.SaveChangesAsync(cancellationToken);

        return record.ToModel();
    }

    public async Task<List<PendingNotificationModel>> GetPendingNotificationsForUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        var records = await _context.PendingNotifications
            .Where(pn => pn.TelegramUserId == telegramUserId)
            .OrderBy(pn => pn.CreatedAt)
            .ToListAsync(cancellationToken);

        return records.Select(r => r.ToModel()).ToList();
    }

    public async Task DeletePendingNotificationAsync(
        long notificationId,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.PendingNotifications
            .FirstOrDefaultAsync(pn => pn.Id == notificationId, cancellationToken);

        if (record != null)
        {
            _context.PendingNotifications.Remove(record);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task IncrementRetryCountAsync(
        long notificationId,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.PendingNotifications
            .FirstOrDefaultAsync(pn => pn.Id == notificationId, cancellationToken);

        if (record != null)
        {
            record.RetryCount++;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteAllPendingNotificationsForUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        var records = await _context.PendingNotifications
            .Where(pn => pn.TelegramUserId == telegramUserId)
            .ToListAsync(cancellationToken);

        if (records.Any())
        {
            _context.PendingNotifications.RemoveRange(records);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> DeleteExpiredNotificationsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredRecords = await _context.PendingNotifications
            .Where(pn => pn.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (expiredRecords.Any())
        {
            _context.PendingNotifications.RemoveRange(expiredRecords);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return expiredRecords.Count;
    }

    public async Task<int> GetPendingNotificationCountAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PendingNotifications
            .Where(pn => pn.TelegramUserId == telegramUserId)
            .CountAsync(cancellationToken);
    }
}
