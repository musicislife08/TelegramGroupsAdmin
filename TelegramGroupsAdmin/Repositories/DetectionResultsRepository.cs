using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class DetectionResultsRepository : IDetectionResultsRepository
{
    private readonly string _connectionString;
    private readonly ILogger<DetectionResultsRepository> _logger;

    public DetectionResultsRepository(
        IConfiguration configuration,
        ILogger<DetectionResultsRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
        _logger = logger;
    }

    public async Task InsertAsync(DetectionResultRecord result)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            INSERT INTO detection_results (
                message_id, detected_at, detection_source, is_spam,
                confidence, reason, detection_method, added_by
            ) VALUES (
                @MessageId, @DetectedAt, @DetectionSource, @IsSpam,
                @Confidence, @Reason, @DetectionMethod, @AddedBy
            );
            """;

        await connection.ExecuteAsync(sql, new
        {
            result.MessageId,
            result.DetectedAt,
            result.DetectionSource,
            result.IsSpam,
            result.Confidence,
            result.Reason,
            result.DetectionMethod,
            result.AddedBy
        });

        _logger.LogDebug(
            "Inserted detection result for message {MessageId}: {IsSpam} (confidence: {Confidence})",
            result.MessageId,
            result.IsSpam ? "spam" : "ham",
            result.Confidence);
    }

    public async Task<DetectionResultRecord?> GetByIdAsync(long id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT dr.id, dr.message_id, dr.detected_at,
                   dr.detection_source, dr.detection_method,
                   dr.is_spam, dr.confidence, dr.reason, dr.added_by,
                   m.user_id, m.message_text
            FROM detection_results dr
            JOIN messages m ON dr.message_id = m.message_id
            WHERE dr.id = @Id;
            """;

        var dto = await connection.QuerySingleOrDefaultAsync<DataModels.DetectionResultRecordDto>(
            sql,
            new { Id = id });

        if (dto == null)
            return null;

        // Get joined data
        var result = dto.ToDetectionResultRecord().ToUiModel();

        // Note: UserId and MessageText are populated by the dynamic query below
        var joinedData = await connection.QuerySingleOrDefaultAsync<(long user_id, string? message_text)>(
            sql,
            new { Id = id });

        result.UserId = joinedData.user_id;
        result.MessageText = joinedData.message_text;

        return result;
    }

    public async Task<List<DetectionResultRecord>> GetByMessageIdAsync(long messageId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT dr.id, dr.message_id, dr.detected_at,
                   dr.detection_source, dr.detection_method,
                   dr.is_spam, dr.confidence, dr.reason, dr.added_by,
                   m.user_id, m.message_text
            FROM detection_results dr
            JOIN messages m ON dr.message_id = m.message_id
            WHERE dr.message_id = @MessageId
            ORDER BY dr.detected_at DESC;
            """;

        var results = await connection.QueryAsync<dynamic>(sql, new { MessageId = messageId });

        return results.Select(r => new DetectionResultRecord
        {
            Id = (long)r.id,
            MessageId = (long)r.message_id,
            DetectedAt = (long)r.detected_at,
            DetectionSource = (string)r.detection_source,
            DetectionMethod = (string)r.detection_method ?? "Unknown",
            IsSpam = (bool)r.is_spam,
            Confidence = (int)(r.confidence ?? 0),
            Reason = r.reason as string,
            AddedBy = r.added_by as string,
            UserId = (long)r.user_id,
            MessageText = r.message_text as string
        }).ToList();
    }

    public async Task<List<DetectionResultRecord>> GetRecentAsync(int limit = 100)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT dr.id, dr.message_id, dr.detected_at,
                   dr.detection_source, dr.detection_method,
                   dr.is_spam, dr.confidence, dr.reason, dr.added_by,
                   m.user_id, m.message_text
            FROM detection_results dr
            JOIN messages m ON dr.message_id = m.message_id
            ORDER BY dr.detected_at DESC
            LIMIT @Limit;
            """;

        var results = await connection.QueryAsync<dynamic>(sql, new { Limit = limit });

        return results.Select(r => new DetectionResultRecord
        {
            Id = (long)r.id,
            MessageId = (long)r.message_id,
            DetectedAt = (long)r.detected_at,
            DetectionSource = (string)r.detection_source,
            DetectionMethod = (string)r.detection_method ?? "Unknown",
            IsSpam = (bool)r.is_spam,
            Confidence = (int)(r.confidence ?? 0),
            Reason = r.reason as string,
            AddedBy = r.added_by as string,
            UserId = (long)r.user_id,
            MessageText = r.message_text as string
        }).ToList();
    }

    public async Task<List<(string MessageText, bool IsSpam)>> GetTrainingSamplesAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Bounded query: all manual samples + recent 10k auto samples
        // This prevents unbounded growth while preserving human-verified data
        const string sql = """
            SELECT m.message_text, dr.is_spam
            FROM detection_results dr
            JOIN messages m ON dr.message_id = m.message_id
            WHERE m.message_text IS NOT NULL
              AND m.message_text != ''
              AND (
                  dr.detection_source = 'manual'
                  OR dr.id IN (
                      SELECT id FROM detection_results
                      WHERE detection_source = 'auto'
                      ORDER BY detected_at DESC
                      LIMIT 10000
                  )
              )
            ORDER BY dr.detected_at DESC;
            """;

        var results = await connection.QueryAsync<(string message_text, bool is_spam)>(sql);

        _logger.LogDebug(
            "Retrieved {Count} training samples for Bayes classifier",
            results.Count());

        return results.Select(r => (r.message_text, r.is_spam)).ToList();
    }

    public async Task<List<string>> GetSpamSamplesForSimilarityAsync(int limit = 1000)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT m.message_text
            FROM detection_results dr
            JOIN messages m ON dr.message_id = m.message_id
            WHERE dr.is_spam = true
              AND m.message_text IS NOT NULL
              AND m.message_text != ''
            ORDER BY dr.detected_at DESC
            LIMIT @Limit;
            """;

        var results = await connection.QueryAsync<string>(sql, new { Limit = limit });

        _logger.LogDebug(
            "Retrieved {Count} spam samples for similarity check",
            results.Count());

        return results.ToList();
    }

    public async Task<bool> IsUserTrustedAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Check for active 'trust' action
        // chatId NULL = global trust, otherwise check specific chat or global
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM user_actions
                WHERE user_id = @UserId
                  AND action_type = 'trust'
                  AND (expires_at IS NULL OR expires_at > @Now)
                  AND (
                      chat_ids IS NULL
                      OR @ChatId = ANY(chat_ids)
                  )
            );
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isTrusted = await connection.ExecuteScalarAsync<bool>(
            sql,
            new { UserId = userId, ChatId = chatId, Now = now });

        if (isTrusted)
        {
            _logger.LogDebug(
                "User {UserId} is trusted (chat: {ChatId})",
                userId,
                chatId?.ToString() ?? "global");
        }

        return isTrusted;
    }

    public async Task<DetectionStats> GetStatsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Overall stats
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

        return new DetectionStats
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

    public async Task<int> DeleteOlderThanAsync(long timestamp)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Note: Per CLAUDE.md, detection_results should be permanent
        // This method exists for completeness but should rarely be used
        const string sql = """
            DELETE FROM detection_results
            WHERE detected_at < @Timestamp;
            """;

        var deleted = await connection.ExecuteAsync(sql, new { Timestamp = timestamp });

        if (deleted > 0)
        {
            _logger.LogWarning(
                "Deleted {Count} old detection results (timestamp < {Timestamp})",
                deleted,
                timestamp);
        }

        return deleted;
    }
}
