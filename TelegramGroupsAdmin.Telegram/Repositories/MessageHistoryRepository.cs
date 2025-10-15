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

        // MH2: Single query optimization - get all expired message data in one query
        var expiredData = await context.Messages
            .Where(m => m.Timestamp < retentionCutoff
                && !context.DetectionResults.Any(dr => dr.MessageId == m.MessageId))
            .GroupJoin(
                context.MessageEdits,
                m => m.MessageId,
                e => e.MessageId,
                (m, edits) => new { Message = m, Edits = edits })
            .Select(x => new
            {
                x.Message,
                EditCount = x.Edits.Count(),
                Edits = x.Edits.ToList()
            })
            .ToListAsync();

        if (expiredData.Count == 0)
        {
            return (0, new List<string>());
        }

        // Collect image paths
        var imagePaths = new List<string>();
        foreach (var data in expiredData)
        {
            if (!string.IsNullOrEmpty(data.Message.PhotoLocalPath))
                imagePaths.Add(data.Message.PhotoLocalPath);
            if (!string.IsNullOrEmpty(data.Message.PhotoThumbnailPath))
                imagePaths.Add(data.Message.PhotoThumbnailPath);
        }

        // Delete edits and messages
        var editsToDelete = expiredData.SelectMany(x => x.Edits).ToList();
        var messagesToDelete = expiredData.Select(x => x.Message).ToList();
        var deletedEdits = editsToDelete.Count;

        context.MessageEdits.RemoveRange(editsToDelete);
        context.Messages.RemoveRange(messagesToDelete);

        await context.SaveChangesAsync();
        var deleted = messagesToDelete.Count;

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
        var results = await context.Messages
            .AsNoTracking()
            .Where(m => m.DeletedAt == null) // Exclude soft-deleted messages
            .GroupJoin(
                context.ManagedChats,
                m => m.ChatId,
                c => c.ChatId,
                (m, chats) => new { Message = m, Chat = chats.FirstOrDefault() })
            .OrderByDescending(x => x.Message.Timestamp)
            .Take(limit)
            .ToListAsync();

        return results.Select(x => x.Message.ToUiModel(
            chatName: x.Chat?.ChatName,
            chatIconPath: x.Chat?.ChatIconPath)).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByChatIdAsync(long chatId, int limit = 10)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var results = await context.Messages
            .AsNoTracking()
            .Where(m => m.ChatId == chatId)
            .GroupJoin(
                context.ManagedChats,
                m => m.ChatId,
                c => c.ChatId,
                (m, chats) => new { Message = m, Chat = chats.FirstOrDefault() })
            .OrderByDescending(x => x.Message.Timestamp)
            .Take(limit)
            .ToListAsync();

        return results.Select(x => x.Message.ToUiModel(
            chatName: x.Chat?.ChatName,
            chatIconPath: x.Chat?.ChatIconPath)).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByDateRangeAsync(
        DateTimeOffset startTimestamp,
        DateTimeOffset endTimestamp,
        int limit = 1000)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var results = await context.Messages
            .AsNoTracking()
            .Where(m => m.Timestamp >= startTimestamp && m.Timestamp <= endTimestamp)
            .GroupJoin(
                context.ManagedChats,
                m => m.ChatId,
                c => c.ChatId,
                (m, chats) => new { Message = m, Chat = chats.FirstOrDefault() })
            .OrderByDescending(x => x.Message.Timestamp)
            .Take(limit)
            .ToListAsync();

        return results.Select(x => x.Message.ToUiModel(
            chatName: x.Chat?.ChatName,
            chatIconPath: x.Chat?.ChatIconPath)).ToList();
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
        var result = await context.Messages
            .AsNoTracking()
            .Where(m => m.MessageId == messageId)
            .GroupJoin(
                context.ManagedChats,
                m => m.ChatId,
                c => c.ChatId,
                (m, chats) => new { Message = m, Chat = chats.FirstOrDefault() })
            .FirstOrDefaultAsync();

        return result?.Message.ToUiModel(
            chatName: result.Chat?.ChatName,
            chatIconPath: result.Chat?.ChatIconPath);
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
        // Query from managed_chats table instead of messages
        var chatNames = await context.ManagedChats
            .AsNoTracking()
            .Where(c => c.ChatName != null && c.ChatName != "")
            .Select(c => c.ChatName!)
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
