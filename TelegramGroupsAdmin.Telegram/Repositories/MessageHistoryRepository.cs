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

    public async Task InsertMessageAsync(UiModels.MessageRecord message, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = message.ToDto();
        context.Messages.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Inserted message {MessageId} from user {UserId} (photo: {HasPhoto})",
            message.MessageId, message.UserId, message.PhotoFileId != null);
    }

    public async Task<UiModels.PhotoMessageRecord?> GetUserRecentPhotoAsync(long userId, long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages
            .AsNoTracking()
            .Where(m => m.UserId == userId
                && m.ChatId == chatId
                && m.PhotoFileId != null)
            .OrderByDescending(m => m.Timestamp)
            .Select(m => new { m.PhotoFileId, m.MessageText, m.Timestamp })
            .FirstOrDefaultAsync(cancellationToken);

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

    public async Task<(int deletedCount, List<string> imagePaths)> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
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
            .ToListAsync(cancellationToken);

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

        await context.SaveChangesAsync(cancellationToken);
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

    public async Task<List<UiModels.MessageRecord>> GetRecentMessagesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var results = await (
            from m in context.Messages
            join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
            from chat in chatGroup.DefaultIfEmpty()
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId into userGroup
            from user in userGroup.DefaultIfEmpty()
            join parent in context.Messages on m.ReplyToMessageId equals parent.MessageId into parentGroup
            from parentMsg in parentGroup.DefaultIfEmpty()
            join parentUser in context.TelegramUsers on parentMsg.UserId equals parentUser.TelegramUserId into parentUserGroup
            from parentUserInfo in parentUserGroup.DefaultIfEmpty()
            orderby m.Timestamp descending
            select new
            {
                Message = m,
                ChatName = chat != null ? chat.ChatName : null,
                ChatIconPath = chat != null ? chat.ChatIconPath : null,
                UserName = user != null ? user.Username : null,
                FirstName = user != null ? user.FirstName : null,
                UserPhotoPath = user != null ? user.UserPhotoPath : null,
                ReplyToUser = parentUserInfo != null ? parentUserInfo.Username : null,
                ReplyToText = parentMsg != null ? parentMsg.MessageText : null
            }
        )
        .AsNoTracking()
        .Take(limit)
        .ToListAsync(cancellationToken);

        return results.Select(x => x.Message.ToModel(
            chatName: x.ChatName,
            chatIconPath: x.ChatIconPath,
            userName: x.UserName,
            firstName: x.FirstName,
            userPhotoPath: x.UserPhotoPath,
            replyToUser: x.ReplyToUser,
            replyToText: x.ReplyToText)).ToList();
    }

    /// <summary>
    /// Get messages before a specific timestamp (cursor-based pagination)
    /// Used for infinite scroll / "Load More" functionality
    /// </summary>
    public async Task<List<UiModels.MessageRecord>> GetMessagesBeforeAsync(
        DateTimeOffset? beforeTimestamp = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = from m in context.Messages
                    join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
                    from chat in chatGroup.DefaultIfEmpty()
                    join u in context.TelegramUsers on m.UserId equals u.TelegramUserId into userGroup
                    from user in userGroup.DefaultIfEmpty()
                    join parent in context.Messages on m.ReplyToMessageId equals parent.MessageId into parentGroup
                    from parentMsg in parentGroup.DefaultIfEmpty()
                    join parentUser in context.TelegramUsers on parentMsg.UserId equals parentUser.TelegramUserId into parentUserGroup
                    from parentUserInfo in parentUserGroup.DefaultIfEmpty()
                    where beforeTimestamp == null || m.Timestamp < beforeTimestamp
                    orderby m.Timestamp descending
                    select new
                    {
                        Message = m,
                        ChatName = chat != null ? chat.ChatName : null,
                        ChatIconPath = chat != null ? chat.ChatIconPath : null,
                        UserName = user != null ? user.Username : null,
                        FirstName = user != null ? user.FirstName : null,
                        UserPhotoPath = user != null ? user.UserPhotoPath : null,
                        ReplyToUser = parentUserInfo != null ? parentUserInfo.Username : null,
                        ReplyToText = parentMsg != null ? parentMsg.MessageText : null
                    };

        var results = await query
            .AsNoTracking()
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results.Select(x => x.Message.ToModel(
            chatName: x.ChatName,
            chatIconPath: x.ChatIconPath,
            userName: x.UserName,
            firstName: x.FirstName,
            userPhotoPath: x.UserPhotoPath,
            replyToUser: x.ReplyToUser,
            replyToText: x.ReplyToText)).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByChatIdAsync(long chatId, int limit = 10, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var results = await (
            from m in context.Messages
            where m.ChatId == chatId
            join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
            from chat in chatGroup.DefaultIfEmpty()
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId into userGroup
            from user in userGroup.DefaultIfEmpty()
            join parent in context.Messages on m.ReplyToMessageId equals parent.MessageId into parentGroup
            from parentMsg in parentGroup.DefaultIfEmpty()
            join parentUser in context.TelegramUsers on parentMsg.UserId equals parentUser.TelegramUserId into parentUserGroup
            from parentUserInfo in parentUserGroup.DefaultIfEmpty()
            orderby m.Timestamp descending
            select new
            {
                Message = m,
                ChatName = chat != null ? chat.ChatName : null,
                ChatIconPath = chat != null ? chat.ChatIconPath : null,
                UserName = user != null ? user.Username : null,
                FirstName = user != null ? user.FirstName : null,
                UserPhotoPath = user != null ? user.UserPhotoPath : null,
                ReplyToUser = parentUserInfo != null ? parentUserInfo.Username : null,
                ReplyToText = parentMsg != null ? parentMsg.MessageText : null
            }
        )
        .AsNoTracking()
        .Take(limit)
        .ToListAsync(cancellationToken);

        return results.Select(x => x.Message.ToModel(
            chatName: x.ChatName,
            chatIconPath: x.ChatIconPath,
            userName: x.UserName,
            firstName: x.FirstName,
            userPhotoPath: x.UserPhotoPath,
            replyToUser: x.ReplyToUser,
            replyToText: x.ReplyToText)).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByDateRangeAsync(
        DateTimeOffset startTimestamp,
        DateTimeOffset endTimestamp,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var results = await (
            from m in context.Messages
            where m.Timestamp >= startTimestamp && m.Timestamp <= endTimestamp
            join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
            from chat in chatGroup.DefaultIfEmpty()
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId into userGroup
            from user in userGroup.DefaultIfEmpty()
            join parent in context.Messages on m.ReplyToMessageId equals parent.MessageId into parentGroup
            from parentMsg in parentGroup.DefaultIfEmpty()
            join parentUser in context.TelegramUsers on parentMsg.UserId equals parentUser.TelegramUserId into parentUserGroup
            from parentUserInfo in parentUserGroup.DefaultIfEmpty()
            orderby m.Timestamp descending
            select new
            {
                Message = m,
                ChatName = chat != null ? chat.ChatName : null,
                ChatIconPath = chat != null ? chat.ChatIconPath : null,
                UserName = user != null ? user.Username : null,
                FirstName = user != null ? user.FirstName : null,
                UserPhotoPath = user != null ? user.UserPhotoPath : null,
                ReplyToUser = parentUserInfo != null ? parentUserInfo.Username : null,
                ReplyToText = parentMsg != null ? parentMsg.MessageText : null
            }
        )
        .AsNoTracking()
        .Take(limit)
        .ToListAsync(cancellationToken);

        return results.Select(x => x.Message.ToModel(
            chatName: x.ChatName,
            chatIconPath: x.ChatIconPath,
            userName: x.UserName,
            firstName: x.FirstName,
            userPhotoPath: x.UserPhotoPath,
            replyToUser: x.ReplyToUser,
            replyToText: x.ReplyToText)).ToList();
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

    public async Task<Dictionary<long, UiModels.SpamCheckRecord>> GetSpamChecksForMessagesAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var messageIdArray = messageIds.ToArray();

        // Query detection_results table (spam_checks table was dropped in normalized schema)
        // Map detection_results fields to SpamCheckRecord for backward compatibility
        // Note: Returns only the LATEST detection result per message for quick display
        // Full detection history is available via GetByMessageIdAsync in DetectionResultsRepository
        var results = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => messageIdArray.Contains(dr.MessageId))
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new
                {
                    dr.Id,
                    dr.MessageId,
                    CheckTimestamp = dr.DetectedAt,
                    m.UserId,
                    m.ContentHash,
                    dr.IsSpam,
                    dr.Confidence,
                    Reason = dr.Reason ?? $"{dr.DetectionMethod}: Spam detected",
                    CheckType = dr.DetectionMethod,
                    MatchedMessageId = dr.MessageId
                })
            .ToListAsync(cancellationToken);

        // Group by message ID and take the latest detection result per message (in-memory)
        var latestResults = results
            .GroupBy(r => r.MessageId)
            .Select(g => g.OrderByDescending(r => r.CheckTimestamp).First())
            .ToList();

        // Get net_confidence values for all these messages (need fresh query to include net_confidence)
        var latestMessageIds = latestResults.Select(r => r.MessageId).Distinct().ToArray();
        var netConfidenceResults = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => latestMessageIds.Contains(dr.MessageId))
            .Select(dr => new { dr.MessageId, dr.NetConfidence, dr.DetectedAt })
            .ToListAsync(cancellationToken);

        // Group detection results by message and take latest net_confidence
        var latestNetConfidence = netConfidenceResults
            .GroupBy(dr => dr.MessageId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(dr => dr.DetectedAt).First().NetConfidence);

        // Build final result with absolute net_confidence as display confidence
        return latestResults
            .Select(r => new UiModels.SpamCheckRecord(
                Id: r.Id,
                CheckTimestamp: r.CheckTimestamp,
                UserId: r.UserId,
                ContentHash: r.ContentHash,
                IsSpam: r.IsSpam,
                Confidence: Math.Abs(latestNetConfidence.GetValueOrDefault(r.MessageId, 0)), // Use absolute net_confidence for display
                Reason: r.Reason,
                CheckType: r.CheckType,
                MatchedMessageId: r.MatchedMessageId))
            .Where(c => c.MatchedMessageId.HasValue)
            .ToDictionary(c => c.MatchedMessageId!.Value, c => c);
    }

    public async Task<Dictionary<long, int>> GetEditCountsForMessagesAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var messageIdArray = messageIds.ToArray();

        var results = await context.MessageEdits
            .AsNoTracking()
            .Where(e => messageIdArray.Contains(e.MessageId))
            .GroupBy(e => e.MessageId)
            .Select(g => new { MessageId = g.Key, EditCount = g.Count() })
            .ToListAsync(cancellationToken);

        return results.ToDictionary(r => r.MessageId, r => r.EditCount);
    }

    public async Task<List<UiModels.MessageEditRecord>> GetEditsForMessageAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.MessageEdits
            .AsNoTracking()
            .Where(e => e.MessageId == messageId)
            .OrderBy(e => e.EditDate)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task InsertMessageEditAsync(UiModels.MessageEditRecord edit, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = edit.ToDto();
        context.MessageEdits.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Inserted edit for message {MessageId} at {EditDate}",
            edit.MessageId,
            edit.EditDate);
    }

    public async Task<UiModels.MessageRecord?> GetMessageAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var result = await (
            from m in context.Messages
            where m.MessageId == messageId
            join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
            from chat in chatGroup.DefaultIfEmpty()
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId into userGroup
            from user in userGroup.DefaultIfEmpty()
            join parent in context.Messages on m.ReplyToMessageId equals parent.MessageId into parentGroup
            from parentMsg in parentGroup.DefaultIfEmpty()
            join parentUser in context.TelegramUsers on parentMsg.UserId equals parentUser.TelegramUserId into parentUserGroup
            from parentUserInfo in parentUserGroup.DefaultIfEmpty()
            select new
            {
                Message = m,
                ChatName = chat != null ? chat.ChatName : null,
                ChatIconPath = chat != null ? chat.ChatIconPath : null,
                UserName = user != null ? user.Username : null,
                FirstName = user != null ? user.FirstName : null,
                UserPhotoPath = user != null ? user.UserPhotoPath : null,
                ReplyToUser = parentUserInfo != null ? parentUserInfo.Username : null,
                ReplyToText = parentMsg != null ? parentMsg.MessageText : null
            }
        )
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

        return result?.Message.ToModel(
            chatName: result.ChatName,
            chatIconPath: result.ChatIconPath,
            userName: result.UserName,
            firstName: result.FirstName,
            userPhotoPath: result.UserPhotoPath,
            replyToUser: result.ReplyToUser,
            replyToText: result.ReplyToText);
    }

    public async Task UpdateMessageAsync(UiModels.MessageRecord message, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync(new object[] { message.MessageId }, cancellationToken);

        if (entity != null)
        {
            entity.MessageText = message.MessageText;
            entity.Urls = message.Urls;
            entity.EditDate = message.EditDate;
            entity.ContentHash = message.ContentHash;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Updated message {MessageId} (edit_date: {EditDate})",
                message.MessageId,
                message.EditDate);
        }
    }

    public async Task<List<string>> GetDistinctUserNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Query from telegram_users table instead of messages
        var userNames = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.Username != null && u.Username != "")
            .Select(u => u.Username!)
            .Distinct()
            .OrderBy(u => u)
            .ToListAsync(cancellationToken);

        return userNames;
    }

    public async Task<List<string>> GetDistinctChatNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Query from managed_chats table instead of messages
        var chatNames = await context.ManagedChats
            .AsNoTracking()
            .Where(c => c.ChatName != null && c.ChatName != "")
            .Select(c => c.ChatName!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        return chatNames;
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
                AddedBy = ModelMappings.ToActor(x.dr.WebUserId, x.dr.TelegramUserId, x.dr.SystemIdentifier, x.ActorWebEmail, x.ActorTelegramUsername, x.ActorTelegramFirstName),
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

    /// <summary>
    /// Mark a message as deleted (soft delete)
    /// </summary>
    public async Task MarkMessageAsDeletedAsync(long messageId, string deletionSource, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync(new object[] { messageId }, cancellationToken);

        if (entity != null)
        {
            entity.DeletedAt = DateTimeOffset.UtcNow;
            entity.DeletionSource = deletionSource;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Marked message {MessageId} as deleted (source: {DeletionSource})",
                messageId,
                deletionSource);
        }
    }
}
