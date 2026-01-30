using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Telegram.Extensions;
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

        var results = await context.EnrichedMessages
            .AsNoTracking()
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results.Select(m => m.ToModel()).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesBeforeAsync(
        DateTimeOffset? beforeTimestamp = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.EnrichedMessages
            .AsNoTracking()
            .Where(m => m.ChatId != 0); // Exclude manual training samples (chat_id=0)

        if (beforeTimestamp.HasValue)
        {
            query = query.Where(m => m.Timestamp < beforeTimestamp.Value);
        }

        var results = await query
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results.Select(m => m.ToModel()).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByChatIdAsync(long chatId, int limit = 10, DateTimeOffset? beforeTimestamp = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.EnrichedMessages
            .AsNoTracking()
            .Where(m => m.ChatId == chatId);

        // Apply timestamp filter for pagination (get messages older than the specified timestamp)
        if (beforeTimestamp.HasValue)
        {
            query = query.Where(m => m.Timestamp < beforeTimestamp.Value);
        }

        var results = await query
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results.Select(m => m.ToModel()).ToList();
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

        // Step 2: Load enriched message data from view (replaces 6-way LINQ JOIN)
        var messageIds = messagesWithDetections.Select(m => m.MessageId).ToArray();
        var userIds = messagesWithDetections.Select(m => m.UserId).Distinct().ToArray();

        var enrichedMessages = await context.EnrichedMessages
            .AsNoTracking()
            .Where(m => messageIds.Contains(m.MessageId))
            .ToListAsync(cancellationToken);

        var enrichedDict = enrichedMessages.ToDictionary(x => x.MessageId);

        // Step 3: Load user tags and notes separately (Phase 4.12: avoid cartesian product)
        var userTags = await (from ut in context.UserTags
                              where userIds.Contains(ut.TelegramUserId) && ut.RemovedAt == null
                              join td in context.TagDefinitions on ut.TagName equals td.TagName into tagGroup
                              from tag in tagGroup.DefaultIfEmpty()
                              // JOIN to get AddedBy actor Telegram user data
                              join addedByUser in context.TelegramUsers on ut.ActorTelegramUserId equals addedByUser.TelegramUserId into addedByGroup
                              from addedBy in addedByGroup.DefaultIfEmpty()
                              // JOIN to get RemovedBy actor Telegram user data
                              join removedByUser in context.TelegramUsers on ut.RemovedByTelegramUserId equals removedByUser.TelegramUserId into removedByGroup
                              from removedBy in removedByGroup.DefaultIfEmpty()
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
                                  ActorTelegramUsername = addedBy != null ? addedBy.Username : null,
                                  ActorTelegramFirstName = addedBy != null ? addedBy.FirstName : null,
                                  ActorTelegramLastName = addedBy != null ? addedBy.LastName : null,
                                  RemovedByWebUserId = ut.RemovedByWebUserId,
                                  RemovedByTelegramUserId = ut.RemovedByTelegramUserId,
                                  RemovedBySystemIdentifier = ut.RemovedBySystemIdentifier,
                                  RemovedByTelegramUsername = removedBy != null ? removedBy.Username : null,
                                  RemovedByTelegramFirstName = removedBy != null ? removedBy.FirstName : null,
                                  RemovedByTelegramLastName = removedBy != null ? removedBy.LastName : null
                              })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var userNotes = await (from an in context.AdminNotes
                               where userIds.Contains(an.TelegramUserId)
                               // JOIN to get CreatedBy actor Telegram user data
                               join createdByUser in context.TelegramUsers on an.ActorTelegramUserId equals createdByUser.TelegramUserId into createdByGroup
                               from createdBy in createdByGroup.DefaultIfEmpty()
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
                                   ActorSystemIdentifier = an.ActorSystemIdentifier,
                                   ActorTelegramUsername = createdBy != null ? createdBy.Username : null,
                                   ActorTelegramFirstName = createdBy != null ? createdBy.FirstName : null,
                                   ActorTelegramLastName = createdBy != null ? createdBy.LastName : null
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

        // Step 4: Combine data (preserve timestamp ordering from Step 1)
        return messagesWithDetections
            .Where(msg => enrichedDict.ContainsKey(msg.MessageId))
            .Select(msg =>
        {
            var enriched = enrichedDict[msg.MessageId];
            var messageModel = enriched.ToModel();

            // Validate media path exists on filesystem (nulls if missing)
            // REFACTOR-3: Now uses shared utility to avoid duplication with MessageHistoryRepository
            var validatedPath = MediaPathUtilities.ValidateMediaPath(
                messageModel.MediaLocalPath,
                (int?)messageModel.MediaType,
                _imageStoragePath,
                out var fullPath);

            if (validatedPath == null && messageModel.MediaLocalPath != null)
            {
                _logger.LogDebug("Media file missing for message {MessageId}: {Path}", messageModel.MessageId, fullPath);
                messageModel = messageModel with { MediaLocalPath = null };
            }

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
                        AddedBy = ActorMappings.ToActor(t.ActorWebUserId, t.ActorTelegramUserId, t.ActorSystemIdentifier, null, t.ActorTelegramUsername, t.ActorTelegramFirstName, t.ActorTelegramLastName),
                        AddedAt = t.AddedAt,
                        RemovedAt = t.RemovedAt,
                        RemovedBy = t.RemovedAt.HasValue
                            ? ActorMappings.ToActor(t.RemovedByWebUserId, t.RemovedByTelegramUserId, t.RemovedBySystemIdentifier, null, t.RemovedByTelegramUsername, t.RemovedByTelegramFirstName, t.RemovedByTelegramLastName)
                            : null
                    })
                    .ToList(),
                UserNotes = notesByUser.GetValueOrDefault(msg.UserId, [])
                    .Select(n => new UiModels.AdminNote
                    {
                        Id = n.Id,
                        TelegramUserId = n.TelegramUserId,
                        NoteText = n.NoteText,
                        CreatedBy = ActorMappings.ToActor(n.ActorWebUserId, n.ActorTelegramUserId, n.ActorSystemIdentifier, null, n.ActorTelegramUsername, n.ActorTelegramFirstName, n.ActorTelegramLastName),
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

        var results = await context.EnrichedMessages
            .AsNoTracking()
            .Where(m => m.Timestamp >= startTimestamp && m.Timestamp <= endTimestamp)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results.Select(m => m.ToModel()).ToList();
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
                    dr.NetConfidence,
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

        // Build final result with absolute net_confidence as display confidence
        return latestResults
            .Select(r => new UiModels.ContentCheckRecord(
                Id: r.Id,
                CheckTimestamp: r.CheckTimestamp,
                UserId: r.UserId,
                ContentHash: r.ContentHash,
                IsSpam: r.IsSpam,
                Confidence: Math.Abs(r.NetConfidence), // Use absolute net_confidence for display
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

    public async Task<UiModels.MessageRecord?> GetMessageByIdAsync(
        UiModels.MessageRecord message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching enriched message {Message}", message.ToLogDebug());

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var result = await context.EnrichedMessages
            .AsNoTracking()
            .Where(m => m.ChatId == message.ChatId && m.MessageId == message.MessageId)
            .FirstOrDefaultAsync(cancellationToken);

        if (result == null)
        {
            _logger.LogWarning("Message not found during enrichment {Message}", message.ToLogDebug());
            return null;
        }

        return result.ToModel();
    }

    /// <inheritdoc />
    public async Task<UiModels.MessageWithDetectionHistory?> GetMessageWithDetectionHistoryAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Step 1: Load message with detection results
        var messageWithDetections = await context.Messages
            .AsNoTracking()
            .Include(m => m.DetectionResults)
            .Where(m => m.MessageId == messageId)
            .FirstOrDefaultAsync(cancellationToken);

        if (messageWithDetections == null)
            return null;

        // Step 2: Load enriched message data from view
        var enrichedMessage = await context.EnrichedMessages
            .AsNoTracking()
            .Where(m => m.MessageId == messageId)
            .FirstOrDefaultAsync(cancellationToken);

        if (enrichedMessage == null)
            return null;

        var messageModel = enrichedMessage.ToModel();

        // Validate media path exists on filesystem
        var validatedPath = MediaPathUtilities.ValidateMediaPath(
            messageModel.MediaLocalPath,
            (int?)messageModel.MediaType,
            _imageStoragePath,
            out var fullPath);

        if (validatedPath == null && messageModel.MediaLocalPath != null)
        {
            _logger.LogDebug("Media file missing for message {MessageId}: {Path}", messageModel.MessageId, fullPath);
            messageModel = messageModel with { MediaLocalPath = null };
        }

        return new UiModels.MessageWithDetectionHistory
        {
            Message = messageModel,
            DetectionResults = messageWithDetections.DetectionResults
                .Select(dr => dr.ToModel())
                .OrderByDescending(dr => dr.DetectedAt)
                .ToList(),
            // Skip user tags/notes for notification - not needed
            UserTags = [],
            UserNotes = []
        };
    }
}
