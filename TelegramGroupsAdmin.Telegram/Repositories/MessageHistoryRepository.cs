using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class MessageHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<MessageHistoryRepository> _logger;

    public MessageHistoryRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<MessageHistoryRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task InsertMessageAsync(UiModels.MessageRecord message)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = message.ToDataModel();
        context.Messages.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogDebug(
            "Inserted message {MessageId} from user {UserId} (photo: {HasPhoto})",
            message.MessageId, message.UserId, message.PhotoFileId != null);
    }

    public async Task<UiModels.PhotoMessageRecord?> GetUserRecentPhotoAsync(long userId, long chatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Messages
            .AsNoTracking()
            .Where(m => m.UserId == userId
                && m.ChatId == chatId
                && m.PhotoFileId != null)
            .OrderByDescending(m => m.Timestamp)
            .Select(m => new { m.PhotoFileId, m.MessageText, m.Timestamp })
            .FirstOrDefaultAsync();

        if (entity == null)
            return null;

        _logger.LogDebug(
            "Found photo {FileId} for user {UserId} in chat {ChatId} from {Timestamp}",
            entity.PhotoFileId, userId, chatId, entity.Timestamp);

        return new UiModels.PhotoMessageRecord(
            FileId: entity.PhotoFileId!,
            MessageText: entity.MessageText,
            Timestamp: entity.Timestamp);
    }

    public async Task<(int deletedCount, List<string> imagePaths)> CleanupExpiredAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Retention: Keep messages from last 30 days OR messages with detection_results (training data)
        var retentionCutoff = DateTimeOffset.UtcNow.AddDays(-30);

        // First, get image paths for messages that will be deleted
        var expiredImages = await context.Messages
            .AsNoTracking()
            .Where(m => m.Timestamp < retentionCutoff
                && !context.DetectionResults.Any(dr => dr.MessageId == m.MessageId))
            .Where(m => m.PhotoLocalPath != null || m.PhotoThumbnailPath != null)
            .Select(m => new { m.PhotoLocalPath, m.PhotoThumbnailPath })
            .ToListAsync();

        // Collect all image paths
        var imagePaths = new List<string>();
        foreach (var img in expiredImages)
        {
            if (!string.IsNullOrEmpty(img.PhotoLocalPath))
                imagePaths.Add(img.PhotoLocalPath);
            if (!string.IsNullOrEmpty(img.PhotoThumbnailPath))
                imagePaths.Add(img.PhotoThumbnailPath);
        }

        // Get messages that will be deleted
        var expiredMessages = await context.Messages
            .Where(m => m.Timestamp < retentionCutoff
                && !context.DetectionResults.Any(dr => dr.MessageId == m.MessageId))
            .ToListAsync();

        if (expiredMessages.Count == 0)
        {
            return (0, imagePaths);
        }

        var expiredMessageIds = expiredMessages.Select(m => m.MessageId).ToList();

        // Delete message edits for expired messages (will cascade delete via FK, but doing explicitly for logging)
        var editsToDelete = await context.MessageEdits
            .Where(e => expiredMessageIds.Contains(e.MessageId))
            .ToListAsync();
        var deletedEdits = editsToDelete.Count;
        context.MessageEdits.RemoveRange(editsToDelete);

        // Delete expired messages (EF Core will handle cascade)
        context.Messages.RemoveRange(expiredMessages);

        await context.SaveChangesAsync();
        var deleted = expiredMessages.Count;

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Cleaned up {Count} old messages ({ImageCount} images, {Edits} edits) - retention: 30 days",
                deleted,
                imagePaths.Count,
                deletedEdits);

            // Note: VACUUM is a PostgreSQL-specific command that can't be run in a transaction
            // EF Core SaveChanges() runs in a transaction, so we skip VACUUM
            // If needed, VACUUM can be run separately via raw SQL outside a transaction
        }

        return (deleted, imagePaths);
    }

    public async Task<List<UiModels.MessageRecord>> GetRecentMessagesAsync(int limit = 100)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.Messages
            .AsNoTracking()
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByChatIdAsync(long chatId, int limit = 10)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.Messages
            .AsNoTracking()
            .Where(m => m.ChatId == chatId)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByDateRangeAsync(
        DateTimeOffset startTimestamp,
        DateTimeOffset endTimestamp,
        int limit = 1000)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.Messages
            .AsNoTracking()
            .Where(m => m.Timestamp >= startTimestamp && m.Timestamp <= endTimestamp)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<UiModels.HistoryStats> GetStatsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var totalMessages = await context.Messages.CountAsync();
        var uniqueUsers = await context.Messages.Select(m => m.UserId).Distinct().CountAsync();
        var photoCount = await context.Messages.CountAsync(m => m.PhotoFileId != null);

        DateTimeOffset? oldestTimestamp = null;
        DateTimeOffset? newestTimestamp = null;

        if (totalMessages > 0)
        {
            oldestTimestamp = await context.Messages.MinAsync(m => m.Timestamp);
            newestTimestamp = await context.Messages.MaxAsync(m => m.Timestamp);
        }

        var result = new UiModels.HistoryStats(
            TotalMessages: totalMessages,
            UniqueUsers: uniqueUsers,
            PhotoCount: photoCount,
            OldestTimestamp: oldestTimestamp,
            NewestTimestamp: newestTimestamp);

        return result;
    }

    public async Task<Dictionary<long, UiModels.SpamCheckRecord>> GetSpamChecksForMessagesAsync(IEnumerable<long> messageIds)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var messageIdArray = messageIds.ToArray();

        // Query detection_results table (spam_checks table was dropped in normalized schema)
        // Map detection_results fields to SpamCheckRecord for backward compatibility
        var results = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => messageIdArray.Contains(dr.MessageId))
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new
                {
                    dr.Id,
                    CheckTimestamp = dr.DetectedAt,
                    m.UserId,
                    m.ContentHash,
                    dr.IsSpam,
                    dr.Confidence,
                    Reason = dr.Reason ?? $"{dr.DetectionMethod}: Spam detected",
                    CheckType = dr.DetectionMethod,
                    MatchedMessageId = dr.MessageId
                })
            .ToListAsync();

        // Return dictionary keyed by matched_message_id
        return results
            .Select(r => new UiModels.SpamCheckRecord(
                Id: r.Id,
                CheckTimestamp: r.CheckTimestamp,
                UserId: r.UserId,
                ContentHash: r.ContentHash,
                IsSpam: r.IsSpam,
                Confidence: r.Confidence,
                Reason: r.Reason,
                CheckType: r.CheckType,
                MatchedMessageId: r.MatchedMessageId))
            .Where(c => c.MatchedMessageId.HasValue)
            .ToDictionary(c => c.MatchedMessageId!.Value, c => c);
    }

    public async Task<Dictionary<long, int>> GetEditCountsForMessagesAsync(IEnumerable<long> messageIds)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var messageIdArray = messageIds.ToArray();

        var results = await context.MessageEdits
            .AsNoTracking()
            .Where(e => messageIdArray.Contains(e.MessageId))
            .GroupBy(e => e.MessageId)
            .Select(g => new { MessageId = g.Key, EditCount = g.Count() })
            .ToListAsync();

        return results.ToDictionary(r => r.MessageId, r => r.EditCount);
    }

    public async Task<List<UiModels.MessageEditRecord>> GetEditsForMessageAsync(long messageId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.MessageEdits
            .AsNoTracking()
            .Where(e => e.MessageId == messageId)
            .OrderBy(e => e.EditDate)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task InsertMessageEditAsync(UiModels.MessageEditRecord edit)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = edit.ToDataModel();
        context.MessageEdits.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogDebug(
            "Inserted edit for message {MessageId} at {EditDate}",
            edit.MessageId,
            edit.EditDate);
    }

    public async Task<UiModels.MessageRecord?> GetMessageAsync(long messageId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.MessageId == messageId);

        return entity?.ToUiModel();
    }

    public async Task UpdateMessageAsync(UiModels.MessageRecord message)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Messages.FindAsync(message.MessageId);

        if (entity != null)
        {
            entity.MessageText = message.MessageText;
            entity.Urls = message.Urls;
            entity.EditDate = message.EditDate;
            entity.ContentHash = message.ContentHash;

            await context.SaveChangesAsync();

            _logger.LogDebug(
                "Updated message {MessageId} with new edit date {EditDate}",
                message.MessageId,
                message.EditDate);
        }
    }

    public async Task<List<string>> GetDistinctUserNamesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var userNames = await context.Messages
            .AsNoTracking()
            .Where(m => m.UserName != null && m.UserName != "")
            .Select(m => m.UserName!)
            .Distinct()
            .OrderBy(u => u)
            .ToListAsync();

        return userNames;
    }

    public async Task<List<string>> GetDistinctChatNamesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var chatNames = await context.Messages
            .AsNoTracking()
            .Where(m => m.ChatName != null && m.ChatName != "")
            .Select(m => m.ChatName!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        return chatNames;
    }

    public async Task<UiModels.DetectionStats> GetDetectionStatsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Overall stats from detection_results
        var allDetections = await context.DetectionResults
            .AsNoTracking()
            .Select(dr => new { dr.IsSpam, dr.Confidence })
            .ToListAsync();

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
            .ToListAsync();

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

    public async Task<List<UiModels.DetectionResultRecord>> GetRecentDetectionsAsync(int limit = 100)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var results = await context.DetectionResults
            .AsNoTracking()
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new
                {
                    dr.Id,
                    dr.MessageId,
                    dr.DetectedAt,
                    dr.DetectionSource,
                    dr.DetectionMethod,
                    dr.IsSpam,
                    dr.Confidence,
                    dr.Reason,
                    m.UserId,
                    m.MessageText
                })
            .OrderByDescending(x => x.DetectedAt)
            .Take(limit)
            .ToListAsync();

        return results.Select(r => new UiModels.DetectionResultRecord
        {
            Id = r.Id,
            MessageId = r.MessageId,
            DetectedAt = r.DetectedAt,
            DetectionSource = r.DetectionSource,
            DetectionMethod = r.DetectionMethod ?? "Unknown",
            IsSpam = r.IsSpam,
            Confidence = r.Confidence,
            Reason = r.Reason,
            UserId = r.UserId,
            MessageText = r.MessageText
        }).ToList();
    }

    /// <summary>
    /// Mark a message as deleted (soft delete)
    /// </summary>
    public async Task MarkMessageAsDeletedAsync(long messageId, string deletionSource)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Messages.FindAsync(messageId);

        if (entity != null)
        {
            entity.DeletedAt = DateTimeOffset.UtcNow;
            entity.DeletionSource = deletionSource;

            await context.SaveChangesAsync();

            _logger.LogDebug(
                "Marked message {MessageId} as deleted (source: {DeletionSource})",
                messageId,
                deletionSource);
        }
    }
}
