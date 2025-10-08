using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Data.Repositories;

// CRITICAL DAPPER/DTO CONVENTION:
// All SQL SELECT statements MUST use raw snake_case column names without aliases.
// DTOs use positional record constructors that are CASE-SENSITIVE.
// See MessageRecord.cs for detailed explanation.

public class SpamCheckRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SpamCheckRepository> _logger;

    public SpamCheckRepository(IConfiguration configuration, ILogger<SpamCheckRepository> logger)
    {
        var dbPath = configuration["MessageHistory:DatabasePath"] ?? "/data/message_history.db";
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public async Task<long> InsertSpamCheckAsync(SpamCheckRecord spamCheck)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            INSERT INTO spam_checks (
                check_timestamp, user_id, content_hash, is_spam, confidence, reason, check_type, matched_message_id
            ) VALUES (
                @CheckTimestamp, @UserId, @ContentHash, @IsSpam, @Confidence, @Reason, @CheckType, @MatchedMessageId
            );
            SELECT last_insert_rowid();
            """;

        var id = await connection.ExecuteScalarAsync<long>(sql, spamCheck);

        _logger.LogDebug(
            "Inserted spam check {Id} for user {UserId}: Spam={IsSpam}, Confidence={Confidence}",
            id,
            spamCheck.UserId,
            spamCheck.IsSpam,
            spamCheck.Confidence);

        return id;
    }

    public async Task<long?> FindMatchedMessageIdAsync(string contentHash, long checkTimestamp)
    {
        await using var connection = new SqliteConnection(_connectionString);

        // Look for message with matching content hash within ±5 seconds
        const string sql = """
            SELECT message_id
            FROM messages
            WHERE content_hash = @ContentHash
              AND timestamp BETWEEN @StartTime AND @EndTime
            ORDER BY ABS(timestamp - @CheckTimestamp)
            LIMIT 1;
            """;

        var messageId = await connection.QuerySingleOrDefaultAsync<long?>(sql, new
        {
            ContentHash = contentHash,
            StartTime = checkTimestamp - 5,
            EndTime = checkTimestamp + 5,
            CheckTimestamp = checkTimestamp
        });

        if (messageId.HasValue)
        {
            _logger.LogDebug(
                "Matched spam check to message {MessageId} via content hash within time window",
                messageId.Value);
        }

        return messageId;
    }

    public async Task<long?> FindMessageByUserAndTimeAsync(long userId, long checkTimestamp)
    {
        await using var connection = new SqliteConnection(_connectionString);

        // Fallback: look for user's message within ±5 seconds
        const string sql = """
            SELECT message_id
            FROM messages
            WHERE user_id = @UserId
              AND timestamp BETWEEN @StartTime AND @EndTime
            ORDER BY ABS(timestamp - @CheckTimestamp)
            LIMIT 1;
            """;

        var messageId = await connection.QuerySingleOrDefaultAsync<long?>(sql, new
        {
            UserId = userId,
            StartTime = checkTimestamp - 5,
            EndTime = checkTimestamp + 5,
            CheckTimestamp = checkTimestamp
        });

        if (messageId.HasValue)
        {
            _logger.LogDebug(
                "Matched spam check to message {MessageId} via user_id + timestamp fallback",
                messageId.Value);
        }

        return messageId;
    }

    public async Task<SpamCheckRecord?> GetSpamCheckByMessageIdAsync(long messageId)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id as Id, check_timestamp as CheckTimestamp, user_id as UserId,
                   content_hash as ContentHash, is_spam as IsSpam, confidence as Confidence,
                   reason as Reason, check_type as CheckType, matched_message_id as MatchedMessageId
            FROM spam_checks
            WHERE matched_message_id = @MessageId
            ORDER BY check_timestamp DESC
            LIMIT 1;
            """;

        var dto = await connection.QuerySingleOrDefaultAsync<SpamCheckRecordDto>(sql, new { MessageId = messageId });
        return dto?.ToSpamCheckRecord();
    }
}
