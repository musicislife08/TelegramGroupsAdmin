using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for complex message queries with JOINs and enrichment
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public class MessageQueryService : IMessageQueryService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<MessageQueryService> _logger;
    private readonly string _imageStoragePath;

    public MessageQueryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<MessageQueryService> logger,
        IOptions<MessageHistoryOptions> messageHistoryOptions)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _imageStoragePath = messageHistoryOptions.Value.ImageStoragePath;
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
                    join trans in context.MessageTranslations on m.MessageId equals trans.MessageId into transGroup
                    from translation in transGroup.DefaultIfEmpty()
                    where m.ChatId != 0 // Exclude manual training samples (chat_id=0)
                       && (beforeTimestamp == null || m.Timestamp < beforeTimestamp)
                       && (translation == null || translation.EditId == null) // Get translation for original message only (not edits)
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
                        ReplyToText = parentMsg != null ? parentMsg.MessageText : null,
                        Translation = translation
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
            replyToText: x.ReplyToText,
            translation: x.Translation?.ToModel())).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByChatIdAsync(long chatId, int limit = 10, DateTimeOffset? beforeTimestamp = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = from m in context.Messages
                    where m.ChatId == chatId
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
                    };

        // Apply timestamp filter for pagination (get messages older than the specified timestamp)
        if (beforeTimestamp.HasValue)
        {
            query = query.Where(x => x.Message.Timestamp < beforeTimestamp.Value);
        }

        var results = await query
            .OrderByDescending(x => x.Message.Timestamp)
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

    public async Task<List<UiModels.MessageWithDetectionHistory>> GetMessagesWithDetectionHistoryAsync(long chatId, int limit = 10, DateTimeOffset? beforeTimestamp = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Step 1: Load messages with detection results (single query with LEFT JOIN)
        var messagesQuery = context.Messages
            .AsNoTracking()
            .Include(m => m.DetectionResults)
            .Where(m => m.ChatId == chatId);

        if (beforeTimestamp.HasValue)
        {
            messagesQuery = messagesQuery.Where(m => m.Timestamp < beforeTimestamp.Value);
        }

        var messagesWithDetections = await messagesQuery
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (!messagesWithDetections.Any())
            return [];

        // Step 2: Load joined data (chat, user, reply info, translations) in single query
        var messageIds = messagesWithDetections.Select(m => m.MessageId).ToArray();
        var userIds = messagesWithDetections.Select(m => m.UserId).Distinct().ToArray();

        var joinedData = await (from m in context.Messages
                                where messageIds.Contains(m.MessageId)
                                join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
                                from chat in chatGroup.DefaultIfEmpty()
                                join u in context.TelegramUsers on m.UserId equals u.TelegramUserId into userGroup
                                from user in userGroup.DefaultIfEmpty()
                                join parent in context.Messages on m.ReplyToMessageId equals parent.MessageId into parentGroup
                                from parentMsg in parentGroup.DefaultIfEmpty()
                                join parentUser in context.TelegramUsers on parentMsg.UserId equals parentUser.TelegramUserId into parentUserGroup
                                from parentUserInfo in parentUserGroup.DefaultIfEmpty()
                                join translation in context.MessageTranslations on m.MessageId equals translation.MessageId into translationGroup
                                from trans in translationGroup.DefaultIfEmpty()
                                select new
                                {
                                    m.MessageId,
                                    m.UserId,
                                    ChatName = chat != null ? chat.ChatName : null,
                                    ChatIconPath = chat != null ? chat.ChatIconPath : null,
                                    UserName = user != null ? user.Username : null,
                                    FirstName = user != null ? user.FirstName : null,
                                    UserPhotoPath = user != null ? user.UserPhotoPath : null,
                                    ReplyToUser = parentUserInfo != null ? parentUserInfo.Username : null,
                                    ReplyToText = parentMsg != null ? parentMsg.MessageText : null,
                                    Translation = trans
                                })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var joinedDict = joinedData.ToDictionary(x => x.MessageId);

        // Step 3: Load user tags and notes separately (Phase 4.12: avoid cartesian product)
        var userTags = await (from ut in context.UserTags
                              where userIds.Contains(ut.TelegramUserId) && ut.RemovedAt == null
                              join td in context.TagDefinitions on ut.TagName equals td.TagName into tagGroup
                              from tag in tagGroup.DefaultIfEmpty()
                              select new
                              {
                                  ut.TelegramUserId,
                                  ut.TagName,
                                  TagColor = tag != null ? tag.Color : Data.Models.TagColor.Primary, // Default to Primary if no definition
                                  ut.AddedAt,
                                  ut.RemovedAt,
                                  // Actor arc pattern columns
                                  ActorWebUserId = ut.ActorWebUserId,
                                  ActorTelegramUserId = ut.ActorTelegramUserId,
                                  ActorSystemIdentifier = ut.ActorSystemIdentifier,
                                  RemovedByWebUserId = ut.RemovedByWebUserId,
                                  RemovedByTelegramUserId = ut.RemovedByTelegramUserId,
                                  RemovedBySystemIdentifier = ut.RemovedBySystemIdentifier
                              })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var userNotes = await (from an in context.AdminNotes
                               where userIds.Contains(an.TelegramUserId)
                               select new
                               {
                                   an.TelegramUserId,
                                   an.Id,
                                   an.NoteText,
                                   an.CreatedAt,
                                   an.UpdatedAt,
                                   an.IsPinned,
                                   // Actor arc pattern columns
                                   ActorWebUserId = an.ActorWebUserId,
                                   ActorTelegramUserId = an.ActorTelegramUserId,
                                   ActorSystemIdentifier = an.ActorSystemIdentifier
                               })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Group tags and notes by user
        var tagsByUser = userTags
            .GroupBy(t => t.TelegramUserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var notesByUser = userNotes
            .GroupBy(n => n.TelegramUserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Step 3: Combine data (preserve timestamp ordering from Step 1)
        return messagesWithDetections.Select(msg =>
        {
            var joined = joinedDict[msg.MessageId];
            var messageModel = msg.ToModel(
                chatName: joined.ChatName,
                chatIconPath: joined.ChatIconPath,
                userName: joined.UserName,
                firstName: joined.FirstName,
                userPhotoPath: joined.UserPhotoPath,
                replyToUser: joined.ReplyToUser,
                replyToText: joined.ReplyToText,
                translation: joined.Translation?.ToModel());

            // Validate media path exists on filesystem (nulls if missing)
            messageModel = ValidateMediaPath(messageModel);

            return new UiModels.MessageWithDetectionHistory
            {
                Message = messageModel,
                DetectionResults = msg.DetectionResults
                    .Select(dr => dr.ToModel())
                    .OrderByDescending(dr => dr.DetectedAt)
                    .ToList(),
                UserTags = tagsByUser.GetValueOrDefault(msg.UserId, [])
                    .Select(t => new UiModels.UserTag
                    {
                        Id = 0, // Not needed for display
                        TelegramUserId = t.TelegramUserId,
                        TagName = t.TagName,
                        TagColor = (UiModels.TagColor)t.TagColor, // Enum cast from Data to UI layer
                        AddedBy = ActorMappings.ToActor(t.ActorWebUserId, t.ActorTelegramUserId, t.ActorSystemIdentifier),
                        AddedAt = t.AddedAt,
                        RemovedAt = t.RemovedAt,
                        RemovedBy = t.RemovedAt.HasValue
                            ? ActorMappings.ToActor(t.RemovedByWebUserId, t.RemovedByTelegramUserId, t.RemovedBySystemIdentifier)
                            : null
                    })
                    .ToList(),
                UserNotes = notesByUser.GetValueOrDefault(msg.UserId, [])
                    .Select(n => new UiModels.AdminNote
                    {
                        Id = n.Id,
                        TelegramUserId = n.TelegramUserId,
                        NoteText = n.NoteText,
                        CreatedBy = ActorMappings.ToActor(n.ActorWebUserId, n.ActorTelegramUserId, n.ActorSystemIdentifier),
                        CreatedAt = n.CreatedAt,
                        UpdatedAt = n.UpdatedAt,
                        IsPinned = n.IsPinned
                    })
                    .ToList()
            };
        }).ToList();
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

    public async Task<Dictionary<long, UiModels.ContentCheckRecord>> GetContentChecksForMessagesAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var messageIdArray = messageIds.ToArray();

        // Query detection_results table (spam_checks table was dropped in normalized schema)
        // Map detection_results fields to ContentCheckRecord for backward compatibility
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
            .Select(r => new UiModels.ContentCheckRecord(
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

    /// <summary>
    /// Validates that media file exists on filesystem, nulls MediaLocalPath if missing
    /// This ensures UI shows placeholders for media that needs re-downloading
    /// </summary>
    private UiModels.MessageRecord ValidateMediaPath(UiModels.MessageRecord message)
    {
        // Skip if no media path set or no media type
        if (string.IsNullOrEmpty(message.MediaLocalPath) || !message.MediaType.HasValue)
            return message;

        // Construct full path using MediaPathUtilities (e.g., /data/media/video/animation_123_ABC.mp4)
        var relativePath = MediaPathUtilities.GetMediaStoragePath(message.MediaLocalPath, (int)message.MediaType.Value);
        var fullPath = Path.Combine(_imageStoragePath, relativePath);

        if (File.Exists(fullPath))
            return message; // File exists, return as-is

        // File missing - null the path so UI shows placeholder and requests download
        _logger.LogDebug("Media file missing for message {MessageId}: {Path}", message.MessageId, fullPath);

        return message with { MediaLocalPath = null };
    }
}
