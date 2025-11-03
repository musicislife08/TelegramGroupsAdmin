using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for message analytics and statistics
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public class MessageStatsService : IMessageStatsService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MessageStatsService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<UiModels.HistoryStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var totalMessages = await context.Messages.CountAsync(cancellationToken);
        var uniqueUsers = await context.Messages.Select(m => m.UserId).Distinct().CountAsync(cancellationToken);
        var photoCount = await context.Messages.CountAsync(m => m.PhotoFileId != null, cancellationToken);

        DateTimeOffset? oldestTimestamp = null;
        DateTimeOffset? newestTimestamp = null;

        if (totalMessages > 0)
        {
            oldestTimestamp = await context.Messages.MinAsync(m => m.Timestamp, cancellationToken);
            newestTimestamp = await context.Messages.MaxAsync(m => m.Timestamp, cancellationToken);
        }

        var result = new UiModels.HistoryStats(
            TotalMessages: totalMessages,
            UniqueUsers: uniqueUsers,
            PhotoCount: photoCount,
            OldestTimestamp: oldestTimestamp,
            NewestTimestamp: newestTimestamp);

        return result;
    }

    public async Task<UiModels.DetectionStats> GetDetectionStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Overall stats from detection_results
        var allDetections = await context.DetectionResults
            .AsNoTracking()
            .Select(dr => new { dr.IsSpam, dr.Confidence })
            .ToListAsync(cancellationToken);

        var total = allDetections.Count;
        var spam = allDetections.Count(d => d.IsSpam);
        var avgConfidence = allDetections.Any()
            ? allDetections.Average(d => (double)d.Confidence)
            : 0.0;

        // Last 24h stats
        var since24h = DateTimeOffset.UtcNow.AddDays(-1);
        var recentDetections = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.DetectedAt >= since24h)
            .Select(dr => dr.IsSpam)
            .ToListAsync(cancellationToken);

        var recentTotal = recentDetections.Count;
        var recentSpam = recentDetections.Count(s => s);

        return new UiModels.DetectionStats
        {
            TotalDetections = total,
            SpamDetected = spam,
            SpamPercentage = total > 0 ? (double)spam / total * 100 : 0,
            AverageConfidence = avgConfidence,
            Last24hDetections = recentTotal,
            Last24hSpam = recentSpam,
            Last24hSpamPercentage = recentTotal > 0 ? (double)recentSpam / recentTotal * 100 : 0
        };
    }

    public async Task<List<UiModels.DetectionResultRecord>> GetRecentDetectionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Join with messages, users, and telegram_users to get actor display names (Phase 4.19)
        var results = await context.DetectionResults
            .AsNoTracking()
            .Join(context.Messages, dr => dr.MessageId, m => m.MessageId, (dr, m) => new { dr, m })
            .GroupJoin(context.Users, x => x.dr.WebUserId, u => u.Id, (x, users) => new { x.dr, x.m, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, user) => new { x.dr, x.m, user })
            .GroupJoin(context.TelegramUsers, x => x.dr.TelegramUserId, tu => tu.TelegramUserId, (x, tgUsers) => new { x.dr, x.m, x.user, tgUsers })
            .SelectMany(x => x.tgUsers.DefaultIfEmpty(), (x, tgUser) => new
            {
                x.dr,
                x.m,
                ActorWebEmail = x.user != null ? x.user.Email : null,
                ActorTelegramUsername = tgUser != null ? tgUser.Username : null,
                ActorTelegramFirstName = tgUser != null ? tgUser.FirstName : null
            })
            .OrderByDescending(x => x.dr.DetectedAt)
            .Take(limit)
            .Select(x => new UiModels.DetectionResultRecord
            {
                Id = x.dr.Id,
                MessageId = x.dr.MessageId,
                DetectedAt = x.dr.DetectedAt,
                DetectionSource = x.dr.DetectionSource,
                DetectionMethod = x.dr.DetectionMethod ?? "Unknown",
                IsSpam = x.dr.IsSpam,
                Confidence = x.dr.Confidence,
                Reason = x.dr.Reason,
                AddedBy = ActorMappings.ToActor(x.dr.WebUserId, x.dr.TelegramUserId, x.dr.SystemIdentifier, x.ActorWebEmail, x.ActorTelegramUsername, x.ActorTelegramFirstName),
                UsedForTraining = x.dr.UsedForTraining,
                NetConfidence = x.dr.NetConfidence,
                CheckResultsJson = x.dr.CheckResultsJson,
                EditVersion = x.dr.EditVersion,
                UserId = x.m.UserId,
                MessageText = x.m.MessageText
            })
            .ToListAsync(cancellationToken);

        return results;
    }

    public async Task<UiModels.MessageTrendsData> GetMessageTrendsAsync(
        List<long> chatIds,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Base query filtered by date and chats
        var baseQuery = context.Messages
            .Where(m => m.Timestamp >= startDate && m.Timestamp <= endDate)
            .Where(m => chatIds.Count == 0 || chatIds.Contains(m.ChatId));

        // Total messages
        var totalMessages = await baseQuery.CountAsync(cancellationToken);

        // Unique users
        var uniqueUsers = await baseQuery
            .Select(m => m.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        // Daily average
        var daysDiff = (endDate - startDate).TotalDays;
        var dailyAverage = daysDiff > 0 ? totalMessages / daysDiff : 0;

        // Spam percentage (messages with spam detection results)
        var spamCount = await (
            from m in baseQuery
            join dr in context.DetectionResults on m.MessageId equals dr.MessageId
            where dr.IsSpam
            select m.MessageId
        ).Distinct().CountAsync(cancellationToken);

        var spamPercentage = totalMessages > 0 ? (spamCount / (double)totalMessages * 100.0) : 0;

        // Daily volume - fetch data and group by user's local date
        var volumeData = await baseQuery
            .Select(m => new { m.Timestamp })
            .ToListAsync(cancellationToken);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var dailyVolume = volumeData
            .GroupBy(m =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new UiModels.DailyVolumeData
            {
                Date = g.Key,
                Count = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToList();

        // Daily spam - fetch spam message data and group by user's local date
        var spamData = await (
            from m in baseQuery
            join dr in context.DetectionResults on m.MessageId equals dr.MessageId
            where dr.IsSpam
            select new { m.Timestamp, m.MessageId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var dailySpam = spamData
            .GroupBy(m =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new UiModels.DailyVolumeData
            {
                Date = g.Key,
                Count = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToList();

        // Daily ham - fetch non-spam message data and group by user's local date
        var hamData = await (
            from m in baseQuery
            where !context.DetectionResults.Any(dr => dr.MessageId == m.MessageId && dr.IsSpam)
            select new { m.Timestamp })
            .ToListAsync(cancellationToken);

        var dailyHam = hamData
            .GroupBy(m =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new UiModels.DailyVolumeData
            {
                Date = g.Key,
                Count = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToList();

        // Per-chat breakdown (only if no specific chats selected)
        var perChatVolume = await (
            from m in baseQuery
            join c in context.ManagedChats on m.ChatId equals c.ChatId
            group m by new { c.ChatId, c.ChatName } into g
            select new UiModels.ChatVolumeData
            {
                ChatName = g.Key.ChatName ?? g.Key.ChatId.ToString(),
                Count = g.Count()
            })
            .OrderByDescending(c => c.Count)
            .ToListAsync(cancellationToken);

        return new UiModels.MessageTrendsData
        {
            TotalMessages = totalMessages,
            DailyAverage = dailyAverage,
            UniqueUsers = uniqueUsers,
            SpamPercentage = spamPercentage,
            DailyVolume = dailyVolume,
            DailySpam = dailySpam,
            DailyHam = dailyHam,
            PerChatVolume = perChatVolume
        };
    }
}
