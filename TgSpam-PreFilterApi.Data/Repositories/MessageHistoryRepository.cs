using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TgSpam_PreFilterApi.Data.Models;

namespace TgSpam_PreFilterApi.Data.Repositories;

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
            SELECT photo_file_id AS FileId, message_text AS MessageText, timestamp AS Timestamp
            FROM messages
            WHERE user_id = @UserId
              AND chat_id = @ChatId
              AND photo_file_id IS NOT NULL
              AND expires_at > @Now
            ORDER BY timestamp DESC
            LIMIT 1;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = await connection.QueryFirstOrDefaultAsync<PhotoMessageRecord>(
            sql,
            new { UserId = userId, ChatId = chatId, Now = now });

        if (result != null)
        {
            _logger.LogDebug(
                "Found photo {FileId} for user {UserId} in chat {ChatId} from {Timestamp}",
                result.FileId, userId, chatId, DateTimeOffset.FromUnixTimeSeconds(result.Timestamp));
        }

        return result;
    }

    public async Task<int> CleanupExpiredAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = "DELETE FROM messages WHERE expires_at <= @Now;";

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var deleted = await connection.ExecuteAsync(sql, new { Now = now });

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired messages", deleted);

            // VACUUM if significant cleanup
            if (deleted > 100)
            {
                await connection.ExecuteAsync("VACUUM;");
                _logger.LogDebug("Executed VACUUM on database");
            }
        }

        return deleted;
    }

    public async Task<HistoryStats> GetStatsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT
                COUNT(*) as TotalMessages,
                COUNT(DISTINCT user_id) as UniqueUsers,
                COUNT(photo_file_id) as PhotoCount,
                COALESCE(MIN(timestamp), 0) as OldestTimestamp,
                COALESCE(MAX(timestamp), 0) as NewestTimestamp
            FROM messages
            WHERE expires_at > @Now;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = await connection.QuerySingleAsync<HistoryStats>(sql, new { Now = now });

        // Return null for timestamps if they're 0 (no messages)
        return result with
        {
            OldestTimestamp = result.OldestTimestamp > 0 ? result.OldestTimestamp : null,
            NewestTimestamp = result.NewestTimestamp > 0 ? result.NewestTimestamp : null
        };
    }
}
