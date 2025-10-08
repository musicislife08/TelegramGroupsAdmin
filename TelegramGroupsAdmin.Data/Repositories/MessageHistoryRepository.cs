using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Data.Repositories;

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
        var dbPath = configuration["MessageHistory:DatabasePath"] ?? "/data/message_history.db";
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public async Task InsertMessageAsync(MessageRecord message)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            INSERT INTO messages (
                message_id, user_id, user_name, chat_id, timestamp, expires_at,
                message_text, photo_file_id, photo_file_size, urls,
                edit_date, content_hash, chat_name, photo_local_path, photo_thumbnail_path
            ) VALUES (
                @MessageId, @UserId, @UserName, @ChatId, @Timestamp, @ExpiresAt,
                @MessageText, @PhotoFileId, @PhotoFileSize, @Urls,
                @EditDate, @ContentHash, @ChatName, @PhotoLocalPath, @PhotoThumbnailPath
            );
            """;

        await connection.ExecuteAsync(sql, message);

        _logger.LogDebug(
            "Inserted message {MessageId} from user {UserId} (photo: {HasPhoto})",
            message.MessageId, message.UserId, message.PhotoFileId != null);
    }

    public async Task<PhotoMessageRecord?> GetUserRecentPhotoAsync(long userId, long chatId)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT photo_file_id AS file_id, message_text AS message_text, timestamp
            FROM messages
            WHERE user_id = @UserId
              AND chat_id = @ChatId
              AND photo_file_id IS NOT NULL
              AND expires_at > @Now
            ORDER BY timestamp DESC
            LIMIT 1;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dto = await connection.QueryFirstOrDefaultAsync<PhotoMessageRecordDto>(
            sql,
            new { UserId = userId, ChatId = chatId, Now = now });

        var result = dto?.ToPhotoMessageRecord();
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
        await using var connection = new SqliteConnection(_connectionString);

        // First, get image paths for messages that will be deleted
        const string selectSql = """
            SELECT photo_local_path, photo_thumbnail_path
            FROM messages
            WHERE expires_at <= @Now
              AND (photo_local_path IS NOT NULL OR photo_thumbnail_path IS NOT NULL);
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiredImages = await connection.QueryAsync<(string? PhotoLocalPath, string? PhotoThumbnailPath)>(
            selectSql,
            new { Now = now });

        // Collect all image paths
        var imagePaths = new List<string>();
        foreach (var (localPath, thumbPath) in expiredImages)
        {
            if (!string.IsNullOrEmpty(localPath))
                imagePaths.Add(localPath);
            if (!string.IsNullOrEmpty(thumbPath))
                imagePaths.Add(thumbPath);
        }

        // First, get list of message IDs that will be deleted (for orphan cleanup)
        const string getExpiredIdsSql = "SELECT message_id FROM messages WHERE expires_at <= @Now;";
        var expiredMessageIds = (await connection.QueryAsync<long>(getExpiredIdsSql, new { Now = now })).ToList();

        if (expiredMessageIds.Count == 0)
        {
            return (0, imagePaths);
        }

        // Delete related records in dependent tables (CASCADE cleanup)
        // This prevents orphaned records in spam_checks and message_edits

        // Delete spam checks for expired messages
        const string deleteSpamChecksSql = "DELETE FROM spam_checks WHERE matched_message_id IN @MessageIds;";
        var deletedSpamChecks = await connection.ExecuteAsync(deleteSpamChecksSql, new { MessageIds = expiredMessageIds });

        // Delete message edits for expired messages
        const string deleteEditsSql = "DELETE FROM message_edits WHERE message_id IN @MessageIds;";
        var deletedEdits = await connection.ExecuteAsync(deleteEditsSql, new { MessageIds = expiredMessageIds });

        // Delete expired messages
        const string deleteSql = "DELETE FROM messages WHERE expires_at <= @Now;";
        var deleted = await connection.ExecuteAsync(deleteSql, new { Now = now });

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Cleaned up {Count} expired messages ({ImageCount} images, {SpamChecks} spam checks, {Edits} edits)",
                deleted,
                imagePaths.Count,
                deletedSpamChecks,
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

    public async Task<List<MessageRecord>> GetRecentMessagesAsync(int limit = 100)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT message_id, user_id, user_name,
                   chat_id, timestamp, expires_at,
                   message_text, photo_file_id,
                   photo_file_size, urls,
                   edit_date, content_hash,
                   chat_name, photo_local_path,
                   photo_thumbnail_path
            FROM messages
            WHERE expires_at > @Now
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dtos = await connection.QueryAsync<MessageRecordDto>(
            sql,
            new { Now = now, Limit = limit });

        return dtos.Select(dto => dto.ToMessageRecord()).ToList();
    }

    public async Task<List<MessageRecord>> GetMessagesByDateRangeAsync(
        long startTimestamp,
        long endTimestamp,
        int limit = 1000)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT message_id, user_id, user_name,
                   chat_id, timestamp, expires_at,
                   message_text, photo_file_id,
                   photo_file_size, urls,
                   edit_date, content_hash,
                   chat_name, photo_local_path,
                   photo_thumbnail_path
            FROM messages
            WHERE timestamp >= @StartTimestamp
              AND timestamp <= @EndTimestamp
              AND expires_at > @Now
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dtos = await connection.QueryAsync<MessageRecordDto>(
            sql,
            new { StartTimestamp = startTimestamp, EndTimestamp = endTimestamp, Now = now, Limit = limit });

        return dtos.Select(dto => dto.ToMessageRecord()).ToList();
    }

    public async Task<HistoryStats> GetStatsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT
                COUNT(*) as total_messages,
                COUNT(DISTINCT user_id) as unique_users,
                COUNT(photo_file_id) as photo_count,
                COALESCE(MIN(timestamp), 0) as oldest_timestamp,
                COALESCE(MAX(timestamp), 0) as newest_timestamp
            FROM messages
            WHERE expires_at > @Now;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dto = await connection.QuerySingleAsync<HistoryStatsDto>(sql, new { Now = now });
        var result = dto.ToHistoryStats();

        // Return null for timestamps if they're 0 (no messages)
        return result with
        {
            OldestTimestamp = result.OldestTimestamp > 0 ? result.OldestTimestamp : null,
            NewestTimestamp = result.NewestTimestamp > 0 ? result.NewestTimestamp : null
        };
    }

    public async Task<Dictionary<long, SpamCheckRecord>> GetSpamChecksForMessagesAsync(IEnumerable<long> messageIds)
    {
        await using var connection = new SqliteConnection(_connectionString);

        // Get all spam checks that have matched_message_id in our list
        const string sql = """
            SELECT id, check_timestamp, user_id,
                   content_hash, is_spam, confidence,
                   reason, check_type, matched_message_id
            FROM spam_checks
            WHERE matched_message_id IN @MessageIds;
            """;

        var dtos = await connection.QueryAsync<SpamCheckRecordDto>(
            sql,
            new { MessageIds = messageIds.ToList() });

        // Return dictionary keyed by matched_message_id
        var checks = dtos.Select(dto => dto.ToSpamCheckRecord());
        return checks
            .Where(c => c.MatchedMessageId.HasValue)
            .ToDictionary(c => c.MatchedMessageId!.Value, c => c);
    }

    public async Task<Dictionary<long, int>> GetEditCountsForMessagesAsync(IEnumerable<long> messageIds)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT message_id, COUNT(*) as edit_count
            FROM message_edits
            WHERE message_id IN @MessageIds
            GROUP BY message_id;
            """;

        var results = await connection.QueryAsync<(long message_id, int edit_count)>(
            sql,
            new { MessageIds = messageIds.ToList() });

        return results.ToDictionary(r => r.message_id, r => r.edit_count);
    }

    public async Task<List<MessageEditRecord>> GetEditsForMessageAsync(long messageId)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, message_id, edit_date,
                   old_text, new_text,
                   old_content_hash, new_content_hash
            FROM message_edits
            WHERE message_id = @MessageId
            ORDER BY edit_date ASC;
            """;

        var dtos = await connection.QueryAsync<MessageEditRecordDto>(sql, new { MessageId = messageId });
        return dtos.Select(dto => dto.ToMessageEditRecord()).ToList();
    }

    public async Task InsertMessageEditAsync(MessageEditRecord edit)
    {
        await using var connection = new SqliteConnection(_connectionString);

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

    public async Task<MessageRecord?> GetMessageAsync(long messageId)
    {
        await using var connection = new SqliteConnection(_connectionString);

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

        var dto = await connection.QuerySingleOrDefaultAsync<MessageRecordDto>(sql, new { MessageId = messageId });
        return dto?.ToMessageRecord();
    }

    public async Task UpdateMessageAsync(MessageRecord message)
    {
        await using var connection = new SqliteConnection(_connectionString);

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
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT DISTINCT user_name
            FROM messages
            WHERE user_name IS NOT NULL
              AND user_name != ''
              AND expires_at > @Now
            ORDER BY user_name ASC;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var userNames = await connection.QueryAsync<string>(sql, new { Now = now });
        return userNames.ToList();
    }

    public async Task<List<string>> GetDistinctChatNamesAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT DISTINCT chat_name
            FROM messages
            WHERE chat_name IS NOT NULL
              AND chat_name != ''
              AND expires_at > @Now
            ORDER BY chat_name ASC;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var chatNames = await connection.QueryAsync<string>(sql, new { Now = now });
        return chatNames.ToList();
    }
}
