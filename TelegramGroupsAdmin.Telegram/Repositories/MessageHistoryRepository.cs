using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
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
        IOptions<AppOptions> appOptions,
        SimHashService simHashService)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _imageStoragePath = appOptions.Value.DataPath;
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
            "Inserted message {MessageId} from user {User} (photo: {HasPhoto})",
            message.MessageId, message.User.ToLogDebug(), message.PhotoFileId != null);
    }


    public async Task<UiModels.MessageCleanupResult> CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Retention: keep messages within the retention period, and any older message whose current
        // verdict is curated training data (explicit labels, confident implicit spam). Media features
        // live on the message row, so they are kept with it.
        var retentionCutoff = DateTimeOffset.UtcNow - retention;

        // MH2: Single query optimization - get all expired message data in one query
        var expiredData = await context.Messages
            .Where(m => m.Timestamp < retentionCutoff
                && !context.MessageVerdicts.Any(v => v.MessageId == m.MessageId && v.ChatId == m.ChatId
                    && VerdictClassifications.CuratedValues.Contains(v.Classification)))
            .GroupJoin(
                context.MessageEdits,
                m => new { m.MessageId, m.ChatId },
                e => new { e.MessageId, e.ChatId },
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
            // No messages to delete - query remaining stats and return
            return await GetCleanupResultAsync(context, 0, [], [], cancellationToken);
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
                "Cleaned up {Count} old messages ({ImageCount} images, {MediaCount} media files, {Edits} edits)",
                deleted,
                imagePaths.Count,
                mediaPaths.Count,
                deletedEdits);

            // Note: VACUUM is a PostgreSQL-specific command that can't be run in a transaction
            // EF Core SaveChanges() runs in a transaction, so we skip VACUUM
            // If needed, VACUUM can be run separately via raw SQL outside a transaction
        }

        // Return cleanup result with remaining stats (repository owns this data)
        return await GetCleanupResultAsync(context, deleted, imagePaths, mediaPaths, cancellationToken);
    }

    /// <summary>
    /// Build cleanup result with remaining message statistics.
    /// The repository is the domain expert for message data - simple COUNTs are appropriate here.
    /// </summary>
    private static async Task<UiModels.MessageCleanupResult> GetCleanupResultAsync(
        AppDbContext context,
        int deletedCount,
        List<string> imagePaths,
        List<string> mediaPaths,
        CancellationToken cancellationToken)
    {
        var remainingMessages = await context.Messages.CountAsync(cancellationToken);
        var remainingUniqueUsers = await context.Messages.Select(m => m.UserId).Distinct().CountAsync(cancellationToken);
        var remainingPhotos = await context.Messages.CountAsync(m => m.PhotoFileId != null, cancellationToken);

        DateTimeOffset? oldestTimestamp = null;
        if (remainingMessages > 0)
        {
            oldestTimestamp = await context.Messages.MinAsync(m => m.Timestamp, cancellationToken);
        }

        return new UiModels.MessageCleanupResult(
            DeletedCount: deletedCount,
            ImagePaths: imagePaths,
            MediaPaths: mediaPaths,
            RemainingMessages: remainingMessages,
            RemainingUniqueUsers: remainingUniqueUsers,
            RemainingPhotos: remainingPhotos,
            OldestTimestamp: oldestTimestamp
        );
    }



    public async Task<UiModels.MessageRecord?> GetMessageAsync(int messageId, long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var result = await (
            from m in context.Messages
            where m.MessageId == messageId && m.ChatId == chatId
            join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
            from chat in chatGroup.DefaultIfEmpty()
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId into userGroup
            from user in userGroup.DefaultIfEmpty()
            join parent in context.Messages
                on new { MessageId = m.ReplyToMessageId, m.ChatId }
                equals new { MessageId = (int?)parent.MessageId, parent.ChatId }
                into parentGroup
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
        var validatedPath = MediaUtilities.ValidateMediaPath(
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

    public async Task UpdateMediaLocalPathAsync(int messageId, long chatId, string localPath, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync([messageId, chatId], cancellationToken);

        if (entity != null)
        {
            entity.MediaLocalPath = localPath;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateMessageTextAsync(int messageId, long chatId, string enrichedText, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync([messageId, chatId], cancellationToken);

        if (entity != null)
        {
            entity.MessageText = enrichedText;
            // Recompute SimHash when text changes
            entity.SimilarityHash = _simHashService.ComputeHash(enrichedText);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateMessageEditDateAsync(int messageId, long chatId, DateTimeOffset editDate, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync([messageId, chatId], cancellationToken);

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
        var entity = await context.Messages.FindAsync([message.MessageId, message.Chat.Id], cancellationToken);

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
    public async Task MarkMessageAsDeletedAsync(int messageId, long chatId, string deletionSource, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.FindAsync([messageId, chatId], cancellationToken);

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
            .AsNoTracking()
            .Where(m => m.UserId == telegramUserId)
            .Where(m => m.DeletedAt == null) // Only non-deleted messages
            .Select(m => new UiModels.UserMessageInfo
            {
                MessageId = m.MessageId,
                ChatId = m.ChatId
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UiModels.RecentMessageVerdict>> GetRecentMessagesWithVerdictAsync(
        long chatId, int count, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await (
            from m in context.Messages.AsNoTracking()
            where m.ChatId == chatId
            join v in context.MessageVerdicts.AsNoTracking() on new { m.MessageId, m.ChatId } equals new { v.MessageId, v.ChatId }
            join tu in context.TelegramUsers.AsNoTracking() on m.UserId equals tu.TelegramUserId into users
            from tu in users.DefaultIfEmpty()
            orderby m.Timestamp descending
            select new { m.MessageId, m.UserId, Username = tu != null ? tu.Username : null, m.MessageText, m.Timestamp, v.IsSpam }
        ).Take(count).ToListAsync(cancellationToken);

        return rows.Select(r => new UiModels.RecentMessageVerdict(r.MessageId, r.UserId, r.Username, r.MessageText, r.Timestamp, r.IsSpam)).ToList();
    }

    public async Task<Dictionary<int, UiModels.ContentCheckRecord>> GetCurrentContentChecksAsync(
        long chatId, IReadOnlyCollection<int> messageIds, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where v.ChatId == chatId && messageIds.Contains(v.MessageId) && v.VerdictId != null
            join dr in context.DetectionResults.AsNoTracking() on v.VerdictId equals (long?)dr.Id
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            select new { dr.DetectedAt, m.UserId, v.IsSpam, dr.Score, dr.Reason, dr.DetectionMethod, v.MessageId }
        ).ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.MessageId,
            r => new UiModels.ContentCheckRecord(
                CheckTimestamp: r.DetectedAt,
                UserId: r.UserId,
                IsSpam: r.IsSpam,
                Score: r.Score,
                Reason: r.Reason ?? $"{r.DetectionMethod}: {(r.IsSpam ? "spam" : "ham")}",
                MatchedMessageId: r.MessageId));
    }
}
