using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

// CRITICAL DAPPER/DTO CONVENTION:
// All SQL SELECT statements MUST use raw snake_case column names without aliases.
// DTOs use positional record constructors that are CASE-SENSITIVE.
//
// ✅ CORRECT:   SELECT message_id, user_id FROM messages
// ❌ INCORRECT: SELECT message_id AS MessageId, user_id AS UserId FROM messages
//
// See MessageRecord.cs for detailed explanation.

public class MessageHistoryRepository
{
    private readonly string _connectionString;
    private readonly ILogger<MessageHistoryRepository> _logger;

    public MessageHistoryRepository(IConfiguration configuration, ILogger<MessageHistoryRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
        _logger = logger;
    }

    public async Task InsertMessageAsync(UiModels.MessageRecord message)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            INSERT INTO messages (
                message_id, user_id, user_name, chat_id, timestamp,
                message_text, photo_file_id, photo_file_size, urls,
                edit_date, content_hash, chat_name, photo_local_path, photo_thumbnail_path
            ) VALUES (
                @MessageId, @UserId, @UserName, @ChatId, @Timestamp,
                @MessageText, @PhotoFileId, @PhotoFileSize, @Urls,
                @EditDate, @ContentHash, @ChatName, @PhotoLocalPath, @PhotoThumbnailPath
            );
            """;

        await connection.ExecuteAsync(sql, message);

        _logger.LogDebug(
            "Inserted message {MessageId} from user {UserId} (photo: {HasPhoto})",
            message.MessageId, message.UserId, message.PhotoFileId != null);
    }

    public async Task<UiModels.PhotoMessageRecord?> GetUserRecentPhotoAsync(long userId, long chatId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT photo_file_id, message_text, timestamp
            FROM messages
            WHERE user_id = @UserId
              AND chat_id = @ChatId
              AND photo_file_id IS NOT NULL
            ORDER BY timestamp DESC
            LIMIT 1;
            """;

        var dto = await connection.QueryFirstOrDefaultAsync<DataModels.PhotoMessageRecordDto>(
            sql,
            new { UserId = userId, ChatId = chatId });

        var result = dto?.ToPhotoMessageRecord().ToUiModel();
        if (result != null)
        {
            _logger.LogDebug(
                "Found photo {FileId} for user {UserId} in chat {ChatId} from {Timestamp}",
                result.FileId, userId, chatId, DateTimeOffset.FromUnixTimeSeconds(result.Timestamp));
        }

        return result;
    }

    public async Task<(int deletedCount, List<string> imagePaths)> CleanupExpiredAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Retention: Keep messages from last 30 days OR messages with detection_results (training data)
        var retentionCutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds();

        // First, get image paths for messages that will be deleted
        const string selectSql = """
            SELECT photo_local_path, photo_thumbnail_path
            FROM messages
            WHERE timestamp < @RetentionCutoff
              AND NOT EXISTS (
                  SELECT 1 FROM detection_results dr
                  WHERE dr.message_id = messages.message_id
              )
              AND (photo_local_path IS NOT NULL OR photo_thumbnail_path IS NOT NULL);
            """;

        var expiredImages = await connection.QueryAsync<(string? PhotoLocalPath, string? PhotoThumbnailPath)>(
            selectSql,
            new { RetentionCutoff = retentionCutoff });

        // Collect all image paths
        var imagePaths = new List<string>();
        foreach (var (localPath, thumbPath) in expiredImages)
        {
            if (!string.IsNullOrEmpty(localPath))
                imagePaths.Add(localPath);
            if (!string.IsNullOrEmpty(thumbPath))
                imagePaths.Add(thumbPath);
        }

        // Get messages that will be deleted (older than retention AND not referenced by detection_results)
        // We keep messages with detection_results for training data and analytics
        const string getExpiredIdsSql = @"
            SELECT m.message_id
            FROM messages m
            WHERE m.timestamp < @RetentionCutoff
              AND NOT EXISTS (
                  SELECT 1 FROM detection_results dr
                  WHERE dr.message_id = m.message_id
              );";
        var expiredMessageIds = (await connection.QueryAsync<long>(getExpiredIdsSql, new { RetentionCutoff = retentionCutoff })).ToList();

        if (expiredMessageIds.Count == 0)
        {
            return (0, imagePaths);
        }

        // Delete related records in dependent tables (CASCADE cleanup)

        // Delete message edits for expired messages
        const string deleteEditsSql = "DELETE FROM message_edits WHERE message_id = ANY(@MessageIds);";
        var deletedEdits = await connection.ExecuteAsync(deleteEditsSql, new { MessageIds = expiredMessageIds.ToArray() });

        // Delete expired messages (only those not referenced by detection_results)
        const string deleteSql = @"
            DELETE FROM messages
            WHERE timestamp < @RetentionCutoff
              AND NOT EXISTS (
                  SELECT 1 FROM detection_results dr
                  WHERE dr.message_id = messages.message_id
              );";
        var deleted = await connection.ExecuteAsync(deleteSql, new { RetentionCutoff = retentionCutoff });

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Cleaned up {Count} old messages ({ImageCount} images, {Edits} edits) - retention: 30 days",
                deleted,
                imagePaths.Count,
                deletedEdits);

            // VACUUM if significant cleanup
            if (deleted > 100)
            {
                await connection.ExecuteAsync("VACUUM;");
                _logger.LogDebug("Executed VACUUM on database");
            }
        }

        return (deleted, imagePaths);
    }

    public async Task<List<UiModels.MessageRecord>> GetRecentMessagesAsync(int limit = 100)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT message_id, user_id, user_name,
                   chat_id, timestamp,
                   message_text, photo_file_id,
                   photo_file_size, urls,
                   edit_date, content_hash,
                   chat_name, photo_local_path,
                   photo_thumbnail_path
            FROM messages
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<DataModels.MessageRecordDto>(
            sql,
            new { Limit = limit });

        return dtos.Select(dto => dto.ToMessageRecord().ToUiModel()).ToList();
    }

    public async Task<List<UiModels.MessageRecord>> GetMessagesByDateRangeAsync(
        long startTimestamp,
        long endTimestamp,
        int limit = 1000)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT message_id, user_id, user_name,
                   chat_id, timestamp,
                   message_text, photo_file_id,
                   photo_file_size, urls,
                   edit_date, content_hash,
                   chat_name, photo_local_path,
                   photo_thumbnail_path
            FROM messages
            WHERE timestamp >= @StartTimestamp
              AND timestamp <= @EndTimestamp
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<DataModels.MessageRecordDto>(
            sql,
            new { StartTimestamp = startTimestamp, EndTimestamp = endTimestamp, Limit = limit });

        return dtos.Select(dto => dto.ToMessageRecord().ToUiModel()).ToList();
    }

    public async Task<UiModels.HistoryStats> GetStatsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT
                COUNT(*) as total_messages,
                COUNT(DISTINCT user_id) as unique_users,
                COUNT(photo_file_id) as photo_count,
                COALESCE(MIN(timestamp), 0) as oldest_timestamp,
                COALESCE(MAX(timestamp), 0) as newest_timestamp
            FROM messages;
            """;

        var dto = await connection.QuerySingleAsync<DataModels.HistoryStatsDto>(sql);
        var result = dto.ToHistoryStats().ToUiModel();

        // Return null for timestamps if they're 0 (no messages)
        return result with
        {
            OldestTimestamp = result.OldestTimestamp > 0 ? result.OldestTimestamp : null,
            NewestTimestamp = result.NewestTimestamp > 0 ? result.NewestTimestamp : null
        };
    }

    public async Task<Dictionary<long, UiModels.SpamCheckRecord>> GetSpamChecksForMessagesAsync(IEnumerable<long> messageIds)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Query detection_results table (spam_checks table was dropped in normalized schema)
        // Map detection_results fields to SpamCheckRecord for backward compatibility
        const string sql = """
            SELECT
                dr.id,
                dr.detected_at as check_timestamp,
                m.user_id,
                m.content_hash,
                dr.is_spam,
                dr.confidence,
                COALESCE(dr.reason, CONCAT(dr.detection_method, ': Spam detected')) as reason,
                dr.detection_method as check_type,
                dr.message_id as matched_message_id
            FROM detection_results dr
            JOIN messages m ON dr.message_id = m.message_id
            WHERE dr.message_id = ANY(@MessageIds);
            """;

        var dtos = await connection.QueryAsync<DataModels.SpamCheckRecordDto>(
            sql,
            new { MessageIds = messageIds.ToArray() });

        // Return dictionary keyed by matched_message_id
        var checks = dtos.Select(dto => dto.ToSpamCheckRecord().ToUiModel());
        return checks
            .Where(c => c.MatchedMessageId.HasValue)
            .ToDictionary(c => c.MatchedMessageId!.Value, c => c);
    }

    public async Task<Dictionary<long, int>> GetEditCountsForMessagesAsync(IEnumerable<long> messageIds)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Note: COUNT(*) requires an alias since it's a computed column without a natural name
        const string sql = """
            SELECT message_id, COUNT(*) as edit_count
            FROM message_edits
            WHERE message_id = ANY(@MessageIds)
            GROUP BY message_id;
            """;

        var results = await connection.QueryAsync<(long message_id, int edit_count)>(
            sql,
            new { MessageIds = messageIds.ToArray() });

        return results.ToDictionary(r => r.message_id, r => r.edit_count);
    }

    public async Task<List<UiModels.MessageEditRecord>> GetEditsForMessageAsync(long messageId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, message_id, edit_date,
                   old_text, new_text,
                   old_content_hash, new_content_hash
            FROM message_edits
            WHERE message_id = @MessageId
            ORDER BY edit_date ASC;
            """;

        var dtos = await connection.QueryAsync<DataModels.MessageEditRecordDto>(sql, new { MessageId = messageId });
        return dtos.Select(dto => dto.ToMessageEditRecord().ToUiModel()).ToList();
    }

    public async Task InsertMessageEditAsync(UiModels.MessageEditRecord edit)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            INSERT INTO message_edits (
                message_id, edit_date, old_text, new_text, old_content_hash, new_content_hash
            ) VALUES (
                @MessageId, @EditDate, @OldText, @NewText, @OldContentHash, @NewContentHash
            );
            """;

        await connection.ExecuteAsync(sql, edit);

        _logger.LogDebug(
            "Inserted edit for message {MessageId} at {EditDate}",
            edit.MessageId,
            edit.EditDate);
    }

    public async Task<UiModels.MessageRecord?> GetMessageAsync(long messageId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT message_id, user_id, user_name,
                   chat_id, timestamp, expires_at,
                   message_text, photo_file_id,
                   photo_file_size, urls, edit_date,
                   content_hash, chat_name,
                   photo_local_path, photo_thumbnail_path
            FROM messages
            WHERE message_id = @MessageId;
            """;

        var dto = await connection.QuerySingleOrDefaultAsync<DataModels.MessageRecordDto>(sql, new { MessageId = messageId });
        return dto?.ToMessageRecord().ToUiModel();
    }

    public async Task UpdateMessageAsync(UiModels.MessageRecord message)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            UPDATE messages
            SET message_text = @MessageText,
                urls = @Urls,
                edit_date = @EditDate,
                content_hash = @ContentHash
            WHERE message_id = @MessageId;
            """;

        await connection.ExecuteAsync(sql, message);

        _logger.LogDebug(
            "Updated message {MessageId} with new edit date {EditDate}",
            message.MessageId,
            message.EditDate);
    }

    public async Task<List<string>> GetDistinctUserNamesAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT DISTINCT user_name
            FROM messages
            WHERE user_name IS NOT NULL
              AND user_name != ''
            ORDER BY user_name ASC;
            """;

        var userNames = await connection.QueryAsync<string>(sql);
        return userNames.ToList();
    }

    public async Task<List<string>> GetDistinctChatNamesAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT DISTINCT chat_name
            FROM messages
            WHERE chat_name IS NOT NULL
              AND chat_name != ''
            ORDER BY chat_name ASC;
            """;

        var chatNames = await connection.QueryAsync<string>(sql);
        return chatNames.ToList();
    }

    public async Task<UiModels.DetectionStats> GetDetectionStatsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Overall stats from detection_results
        const string countSql = """
            SELECT
                CAST(COUNT(*) AS INTEGER) as total,
                CAST(COALESCE(SUM(CASE WHEN is_spam = true THEN 1 ELSE 0 END), 0) AS INTEGER) as spam,
                AVG(CAST(confidence AS DOUBLE PRECISION)) as avg_confidence
            FROM detection_results;
            """;

        // Last 24h stats
        const string recentSql = """
            SELECT
                CAST(COUNT(*) AS INTEGER) as total,
                CAST(COALESCE(SUM(CASE WHEN is_spam = true THEN 1 ELSE 0 END), 0) AS INTEGER) as spam
            FROM detection_results
            WHERE detected_at >= @Since;
            """;

        var since24h = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();

        var countResult = await connection.QuerySingleAsync(countSql);
        var recentResult = await connection.QuerySingleAsync(recentSql, new { Since = since24h });

        var total = (int)(countResult.total ?? 0);
        var spam = (int)(countResult.spam ?? 0);
        var recentTotal = (int)(recentResult.total ?? 0);
        var recentSpam = (int)(recentResult.spam ?? 0);

        return new UiModels.DetectionStats
        {
            TotalDetections = total,
            SpamDetected = spam,
            SpamPercentage = total > 0 ? (double)spam / total * 100 : 0,
            AverageConfidence = countResult.avg_confidence != null ? (double)countResult.avg_confidence : 0,
            Last24hDetections = recentTotal,
            Last24hSpam = recentSpam,
            Last24hSpamPercentage = recentTotal > 0 ? (double)recentSpam / recentTotal * 100 : 0
        };
    }

    public async Task<List<UiModels.DetectionResultRecord>> GetRecentDetectionsAsync(int limit = 100)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT dr.id, dr.message_id, dr.detected_at,
                   dr.detection_source, dr.detection_method,
                   dr.is_spam, dr.confidence, dr.reason,
                   m.user_id, m.message_text
            FROM detection_results dr
            JOIN messages m ON dr.message_id = m.message_id
            ORDER BY dr.detected_at DESC
            LIMIT @Limit;
            """;

        var results = await connection.QueryAsync<dynamic>(sql, new { Limit = limit });

        return results.Select(r => new UiModels.DetectionResultRecord
        {
            Id = (long)r.id,
            MessageId = (long)r.message_id,
            DetectedAt = (long)r.detected_at,
            DetectionSource = (string)r.detection_source,
            DetectionMethod = (string)r.detection_method ?? "Unknown",
            IsSpam = (bool)r.is_spam,
            Confidence = (int)(r.confidence ?? 0),
            Details = r.reason as string,
            UserId = (long)r.user_id,
            MessageText = r.message_text as string
        }).ToList();
    }
}
