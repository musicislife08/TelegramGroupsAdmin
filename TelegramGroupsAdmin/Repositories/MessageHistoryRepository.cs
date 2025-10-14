using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

public class MessageHistoryRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<MessageHistoryRepository> _logger;

    public MessageHistoryRepository(AppDbContext context, ILogger<MessageHistoryRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task InsertMessageAsync(UiModels.MessageRecord message)
    {
        var entity = message.ToDataModel();
        _context.Messages.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogDebug(
            "Inserted message {MessageId} from user {UserId} (photo: {HasPhoto})",
            message.MessageId, message.UserId, message.PhotoFileId != null);
    }

    public async Task<UiModels.PhotoMessageRecord?> GetUserRecentPhotoAsync(long userId, long chatId)
    {
        var entity = await _context.Messages
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
            entity.PhotoFileId, userId, chatId, DateTimeOffset.FromUnixTimeSeconds(entity.Timestamp));

        return new UiModels.PhotoMessageRecord(
            FileId: entity.PhotoFileId!,
            MessageText: entity.MessageText,
            Timestamp: entity.Timestamp);
    }

    public async Task<(int deletedCount, List<string> imagePaths)> CleanupExpiredAsync()
    {
        // Retention: Keep messages from last 30 days OR messages with detection_results (training data)
        var retentionCutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds();

        // First, get image paths for messages that will be deleted
        var expiredImages = await _context.Messages
            .AsNoTracking()
            .Where(m => m.Timestamp < retentionCutoff
                && !_context.DetectionResults.Any(dr => dr.MessageId == m.MessageId))
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
        var expiredMessages = await _context.Messages
            .Where(m => m.Timestamp < retentionCutoff
                && !_context.DetectionResults.Any(dr => dr.MessageId == m.MessageId))
            .ToListAsync();

        if (expiredMessages.Count == 0)
        {
            return (0, imagePaths);
        }

        var expiredMessageIds = expiredMessages.Select(m => m.MessageId).ToList();

        // Delete message edits for expired messages (will cascade delete via FK, but doing explicitly for logging)
        var editsToDelete = await _context.MessageEdits
            .Where(e => expiredMessageIds.Contains(e.MessageId))
            .ToListAsync();
        var deletedEdits = editsToDelete.Count;
        _context.MessageEdits.RemoveRange(editsToDelete);

        // Delete expired messages (EF Core will handle cascade)
        _context.Messages.RemoveRange(expiredMessages);

        await _context.SaveChangesAsync();
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
        var entities = await _context.Messages
            .AsNoTracking()
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByChatIdAsync(long chatId, int limit = 10)
    {
        var entities = await _context.Messages
            .AsNoTracking()
            .Where(m => m.ChatId == chatId)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByDateRangeAsync(
        long startTimestamp,
        long endTimestamp,
        int limit = 1000)
    {
        var entities = await _context.Messages
            .AsNoTracking()
            .Where(m => m.Timestamp >= startTimestamp && m.Timestamp <= endTimestamp)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<UiModels.HistoryStats> GetStatsAsync()
    {
        var totalMessages = await _context.Messages.CountAsync();
        var uniqueUsers = await _context.Messages.Select(m => m.UserId).Distinct().CountAsync();
        var photoCount = await _context.Messages.CountAsync(m => m.PhotoFileId != null);

        long oldestTimestamp = 0;
        long newestTimestamp = 0;

        if (totalMessages > 0)
        {
            oldestTimestamp = await _context.Messages.MinAsync(m => m.Timestamp);
            newestTimestamp = await _context.Messages.MaxAsync(m => m.Timestamp);
        }

        var result = new UiModels.HistoryStats(
            TotalMessages: totalMessages,
            UniqueUsers: uniqueUsers,
            PhotoCount: photoCount,
            OldestTimestamp: oldestTimestamp > 0 ? oldestTimestamp : null,
            NewestTimestamp: newestTimestamp > 0 ? newestTimestamp : null);

        return result;
    }

    public async Task<Dictionary<long, UiModels.SpamCheckRecord>> GetSpamChecksForMessagesAsync(IEnumerable<long> messageIds)
    {
        var messageIdArray = messageIds.ToArray();

        // Query detection_results table (spam_checks table was dropped in normalized schema)
        // Map detection_results fields to SpamCheckRecord for backward compatibility
        var results = await _context.DetectionResults
            .AsNoTracking()
            .Where(dr => messageIdArray.Contains(dr.MessageId))
            .Join(_context.Messages,
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
        var messageIdArray = messageIds.ToArray();

        var results = await _context.MessageEdits
            .AsNoTracking()
            .Where(e => messageIdArray.Contains(e.MessageId))
            .GroupBy(e => e.MessageId)
            .Select(g => new { MessageId = g.Key, EditCount = g.Count() })
            .ToListAsync();

        return results.ToDictionary(r => r.MessageId, r => r.EditCount);
    }

    public async Task<List<UiModels.MessageEditRecord>> GetEditsForMessageAsync(long messageId)
    {
        var entities = await _context.MessageEdits
            .AsNoTracking()
            .Where(e => e.MessageId == messageId)
            .OrderBy(e => e.EditDate)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task InsertMessageEditAsync(UiModels.MessageEditRecord edit)
    {
        var entity = edit.ToDataModel();
        _context.MessageEdits.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogDebug(
            "Inserted edit for message {MessageId} at {EditDate}",
            edit.MessageId,
            edit.EditDate);
    }

    public async Task<UiModels.MessageRecord?> GetMessageAsync(long messageId)
    {
        var entity = await _context.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.MessageId == messageId);

        return entity?.ToUiModel();
    }

    public async Task UpdateMessageAsync(UiModels.MessageRecord message)
    {
        var entity = await _context.Messages.FindAsync(message.MessageId);

        if (entity != null)
        {
            entity.MessageText = message.MessageText;
            entity.Urls = message.Urls;
            entity.EditDate = message.EditDate;
            entity.ContentHash = message.ContentHash;

            await _context.SaveChangesAsync();

            _logger.LogDebug(
                "Updated message {MessageId} with new edit date {EditDate}",
                message.MessageId,
                message.EditDate);
        }
    }

    public async Task<List<string>> GetDistinctUserNamesAsync()
    {
        var userNames = await _context.Messages
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
        var chatNames = await _context.Messages
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
        // Overall stats from detection_results
        var allDetections = await _context.DetectionResults
            .AsNoTracking()
            .Select(dr => new { dr.IsSpam, dr.Confidence })
            .ToListAsync();

        var total = allDetections.Count;
        var spam = allDetections.Count(d => d.IsSpam);
        var avgConfidence = allDetections.Any()
            ? allDetections.Average(d => (double)d.Confidence)
            : 0.0;

        // Last 24h stats
        var since24h = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var recentDetections = await _context.DetectionResults
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
        var results = await _context.DetectionResults
            .AsNoTracking()
            .Join(_context.Messages,
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
        var entity = await _context.Messages.FindAsync(messageId);

        if (entity != null)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            entity.DeletedAt = now;
            entity.DeletionSource = deletionSource;

            await _context.SaveChangesAsync();

            _logger.LogDebug(
                "Marked message {MessageId} as deleted (source: {DeletionSource})",
                messageId,
                deletionSource);
        }
    }
}
