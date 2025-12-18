using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Telegram.Services;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class MessageHistoryRepository : IMessageHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<MessageHistoryRepository> _logger;
    private readonly string _imageStoragePath;
    private readonly SimHashService _simHashService;

    public MessageHistoryRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<MessageHistoryRepository> logger,
        IOptions<MessageHistoryOptions> messageHistoryOptions,
        SimHashService simHashService)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _imageStoragePath = messageHistoryOptions.Value.ImageStoragePath;
        _simHashService = simHashService;
    }

    public async Task InsertMessageAsync(UiModels.MessageRecord message, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = message.ToDto();

        // Compute SimHash for near-duplicate detection in training data
        entity.SimilarityHash = _simHashService.ComputeHash(message.MessageText);

        context.Messages.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Inserted message {MessageId} from user {UserId} (photo: {HasPhoto})",
            message.MessageId, message.UserId, message.PhotoFileId != null);
    }


    public async Task<(int deletedCount, List<string> imagePaths, List<string> mediaPaths)> CleanupExpiredAsync(CancellationToken cancellationToken = default)
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
            return (0, [], []);
        }

        // Collect image paths (photo thumbnails)
        var imagePaths = new List<string>();
        // Collect media paths (videos, animations, audio, voice, stickers, video notes)
        var mediaPaths = new List<string>();

        foreach (var data in expiredData)
        {
            // Photo thumbnails
            if (!string.IsNullOrEmpty(data.Message.PhotoLocalPath))
                imagePaths.Add(data.Message.PhotoLocalPath);
            if (!string.IsNullOrEmpty(data.Message.PhotoThumbnailPath))
                imagePaths.Add(data.Message.PhotoThumbnailPath);

            // Media files (Animation, Video, Audio, Voice, Sticker, VideoNote)
            // Note: Documents are excluded - they're metadata-only and never downloaded for display
            if (!string.IsNullOrEmpty(data.Message.MediaLocalPath))
                mediaPaths.Add(data.Message.MediaLocalPath);
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
                "Cleaned up {Count} old messages ({ImageCount} images, {MediaCount} media files, {Edits} edits) - retention: 30 days",
                deleted,
                imagePaths.Count,
                mediaPaths.Count,
                deletedEdits);

            // Note: VACUUM is a PostgreSQL-specific command that can't be run in a transaction
            // EF Core SaveChanges() runs in a transaction, so we skip VACUUM
            // If needed, VACUUM can be run separately via raw SQL outside a transaction
        }

        return (deleted, imagePaths, mediaPaths);
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
                LastName = user != null ? user.LastName : null,
                UserPhotoPath = user != null ? user.UserPhotoPath : null,
                ParentUserFirstName = parentUserInfo != null ? parentUserInfo.FirstName : null,
                ParentUserLastName = parentUserInfo != null ? parentUserInfo.LastName : null,
                ParentUserUsername = parentUserInfo != null ? parentUserInfo.Username : null,
                ParentUserId = parentUserInfo != null ? parentUserInfo.TelegramUserId : (long?)null,
                ReplyToText = parentMsg != null ? parentMsg.MessageText : null
            }
        )
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

        if (result == null)
            return null;

        var messageModel = result.Message.ToModel(
            chatName: result.ChatName,
            chatIconPath: result.ChatIconPath,
            userName: result.UserName,
            firstName: result.FirstName,
            lastName: result.LastName,
            userPhotoPath: result.UserPhotoPath,
            replyToUser: TelegramDisplayName.Format(result.ParentUserFirstName, result.ParentUserLastName, result.ParentUserUsername, result.ParentUserId),
            replyToText: result.ReplyToText);

        // Validate media path exists on filesystem (nulls if missing)
        // REFACTOR-3: Now uses shared utility to avoid duplication with MessageQueryService
        var validatedPath = MediaPathUtilities.ValidateMediaPath(
            messageModel.MediaLocalPath,
            (int?)messageModel.MediaType,
            _imageStoragePath,
            out var fullPath);

        if (validatedPath == null && messageModel.MediaLocalPath != null)
        {
            _logger.LogDebug("Media file missing for message {MessageId}: {Path}", messageModel.MessageId, fullPath);
            return messageModel with { MediaLocalPath = null };
        }

        return messageModel;
    }

    public async Task UpdateMediaLocalPathAsync(long messageId, string localPath, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync([messageId], cancellationToken);

        if (entity != null)
        {
            entity.MediaLocalPath = localPath;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateMessageTextAsync(long messageId, string enrichedText, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync([messageId], cancellationToken);

        if (entity != null)
        {
            entity.MessageText = enrichedText;
            // Recompute SimHash when text changes
            entity.SimilarityHash = _simHashService.ComputeHash(enrichedText);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateMessageEditDateAsync(long messageId, DateTimeOffset editDate, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync([messageId], cancellationToken);

        if (entity != null)
        {
            entity.EditDate = editDate;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Updated message {MessageId} edit_date to {EditDate}",
                messageId,
                editDate);
        }
    }

    public async Task UpdateMessageAsync(UiModels.MessageRecord message, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync([message.MessageId], cancellationToken);

        if (entity != null)
        {
            entity.MessageText = message.MessageText;
            entity.Urls = message.Urls;
            entity.EditDate = message.EditDate;
            entity.ContentHash = message.ContentHash;
            // Recompute SimHash when text changes from edit
            entity.SimilarityHash = _simHashService.ComputeHash(message.MessageText);

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Updated message {MessageId} (edit_date: {EditDate})",
                message.MessageId,
                message.EditDate);
        }
    }


    /// <summary>
    /// Mark a message as deleted (soft delete)
    /// </summary>
    public async Task MarkMessageAsDeletedAsync(long messageId, string deletionSource, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync([messageId], cancellationToken);

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

    /// <summary>
    /// Gets the number of messages a user has sent in a specific chat
    /// Used for impersonation detection (check first N messages)
    /// </summary>
    public async Task<int> GetMessageCountAsync(long userId, long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Messages
            .AsNoTracking()
            .CountAsync(m => m.UserId == userId && m.ChatId == chatId, cancellationToken);
    }

    public async Task<int> GetMessageCountByChatIdAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Messages
            .AsNoTracking()
            .CountAsync(m => m.ChatId == chatId, cancellationToken);
    }


    public async Task<List<UiModels.UserMessageInfo>> GetUserMessagesAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Messages
            .Where(m => m.UserId == telegramUserId)
            .Where(m => m.DeletedAt == null) // Only non-deleted messages
            .Select(m => new UiModels.UserMessageInfo
            {
                MessageId = m.MessageId,
                ChatId = m.ChatId
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Check if a similar SimHash exists in training data using Hamming distance.
    /// </summary>
    /// <remarks>
    /// Raw SQL Required: PostgreSQL's bit_count() for Hamming distance calculation cannot be expressed in EF Core:
    /// - bit_count() function is PostgreSQL-specific (no EF Core equivalent)
    /// - XOR operator (#) is PostgreSQL-specific
    /// - Cast to bit(64) is PostgreSQL-specific
    /// This is the same pattern used in AnalyticsRepository for JSONB queries and TrainingLabelsRepository for upserts.
    /// </remarks>
    public async Task<bool> HasSimilarTrainingHashAsync(long hash, bool isSpam, int maxDistance = 10, CancellationToken cancellationToken = default)
    {
        if (hash == 0)
            return false; // Empty/null text has no hash to compare

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // TrainingLabel enum: Spam=0, Ham=1
        var labelValue = isSpam ? 0 : 1;

        // Training data comes from two sources:
        // 1. detection_results with used_for_training=true (implicit labels from auto-detection)
        // 2. training_labels (explicit labels from manual labeling)
        // COALESCE prefers translation hash (analyzed text) over original message hash
        var result = await context.Database
            .SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1
                    FROM messages m
                    LEFT JOIN message_translations mt ON mt.message_id = m.message_id AND mt.edit_id IS NULL
                    WHERE COALESCE(mt.similarity_hash, m.similarity_hash) IS NOT NULL
                      AND bit_count((COALESCE(mt.similarity_hash, m.similarity_hash) # {hash})::bit(64))::int <= {maxDistance}
                      AND (
                          EXISTS (
                              SELECT 1 FROM detection_results dr
                              WHERE dr.message_id = m.message_id
                                AND dr.is_spam = {isSpam}
                                AND dr.used_for_training = true
                          )
                          OR
                          EXISTS (
                              SELECT 1 FROM training_labels tl
                              WHERE tl.message_id = m.message_id
                                AND tl.label = {labelValue}
                          )
                      )
                ) AS "Value"
                """)
            .FirstAsync(cancellationToken);

        return result;
    }
}
