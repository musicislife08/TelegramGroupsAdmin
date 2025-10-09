using Npgsql;
using Dapper;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.SpamDetection.Repositories;

// CRITICAL DAPPER/DTO CONVENTION:
// All SQL SELECT statements MUST use raw snake_case column names without aliases.
// DTOs use init-only property setters for materialization.
//
// ✅ CORRECT:   SELECT id, message_text FROM detection_results
// ❌ INCORRECT: SELECT id AS Id, message_text AS MessageText FROM detection_results
//
// NOTE: This repository queries the new normalized schema (detection_results + messages)
// but maintains the old interface for backward compatibility during migration.

/// <summary>
/// Repository implementation for spam detection results (formerly training samples)
/// Queries detection_results table joined with messages table
/// </summary>
public class TrainingSamplesRepository : ITrainingSamplesRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<TrainingSamplesRepository> _logger;

    public TrainingSamplesRepository(NpgsqlDataSource dataSource, ILogger<TrainingSamplesRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Get all detection results (spam and ham samples)
    /// </summary>
    public async Task<IEnumerable<TrainingSample>> GetAllSamplesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT dr.id, m.message_text, dr.is_spam, dr.detected_at as added_date,
                       dr.detection_source as source, dr.confidence as confidence_when_added,
                       ARRAY[m.chat_id] as chat_ids,
                       COALESCE(u.email, 'Unknown') as added_by,
                       0 as detection_count, NULL::bigint as last_detected_date
                FROM detection_results dr
                JOIN messages m ON dr.message_id = m.message_id
                LEFT JOIN users u ON dr.added_by = u.id
                ORDER BY dr.detected_at DESC";

            var dtos = await connection.QueryAsync<TrainingSampleDto>(sql);
            return dtos.Select(dto => dto.ToTrainingSample());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve detection results");
            return Enumerable.Empty<TrainingSample>();
        }
    }

    /// <summary>
    /// Get only spam detection results (is_spam = true)
    /// Used by Similarity spam check for TF-IDF matching
    /// All spam is global - no per-chat filtering needed
    /// </summary>
    public async Task<IEnumerable<TrainingSample>> GetSpamSamplesAsync(string? chatId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT dr.id, m.message_text, dr.is_spam, dr.detected_at as added_date,
                       dr.detection_source as source, dr.confidence as confidence_when_added,
                       ARRAY[m.chat_id] as chat_ids, dr.added_by,
                       0 as detection_count, NULL::bigint as last_detected_date
                FROM detection_results dr
                JOIN messages m ON dr.message_id = m.message_id
                WHERE dr.is_spam = true
                ORDER BY dr.detected_at DESC";

            var dtos = await connection.QueryAsync<TrainingSampleDto>(sql);
            return dtos.Select(dto => dto.ToTrainingSample());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve spam detection results");
            return Enumerable.Empty<TrainingSample>();
        }
    }

    /// <summary>
    /// Increment detection count for a sample when it successfully detects spam/ham
    /// NOTE: Detection count tracking removed in normalized schema
    /// This method is now a no-op for backward compatibility
    /// </summary>
    public async Task IncrementDetectionCountAsync(long sampleId, CancellationToken cancellationToken = default)
    {
        // No-op: detection_count column removed in normalized schema
        // Analytics will be done via COUNT(*) on detection_results table
        await Task.CompletedTask;
        _logger.LogDebug("IncrementDetectionCountAsync called for sample {SampleId} (no-op in new schema)", sampleId);
    }

    /// <summary>
    /// Add a new detection result (spam/ham sample)
    /// Creates or finds message, then adds detection_result record
    /// </summary>
    public async Task<long> AddSampleAsync(string messageText, bool isSpam, string source, int? confidenceWhenAdded = null, string? chatId = null, string? addedBy = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var chatIdLong = chatId != null ? long.Parse(chatId) : -1; // -1 for unknown chat

                // Step 1: Insert or find message
                const string insertMessageSql = @"
                    INSERT INTO messages (message_id, chat_id, user_id, user_name, timestamp, message_text, content_hash)
                    VALUES (
                        (SELECT COALESCE(MIN(message_id), 0) - 1 FROM messages WHERE message_id < 0), -- Next negative ID
                        @ChatId, -1, 'Manual', @Timestamp, @MessageText, MD5(@MessageText)
                    )
                    ON CONFLICT (message_id) DO NOTHING
                    RETURNING message_id";

                // Try to find existing message by text and timestamp
                const string findMessageSql = @"
                    SELECT message_id FROM messages
                    WHERE message_text = @MessageText AND timestamp = @Timestamp
                    LIMIT 1";

                long messageId;
                var existingMessageId = await connection.QuerySingleOrDefaultAsync<long?>(findMessageSql, new { MessageText = messageText, Timestamp = timestamp }, transaction);

                if (existingMessageId.HasValue)
                {
                    messageId = existingMessageId.Value;
                }
                else
                {
                    messageId = await connection.QuerySingleAsync<long>(insertMessageSql, new { ChatId = chatIdLong, Timestamp = timestamp, MessageText = messageText }, transaction);
                }

                // Step 2: Insert detection_result
                const string insertDetectionSql = @"
                    INSERT INTO detection_results (message_id, detected_at, detection_source, is_spam, confidence, added_by, detection_method)
                    VALUES (@MessageId, @DetectedAt, @Source, @IsSpam, @Confidence, @AddedBy, 'Manual')
                    RETURNING id";

                var detectionId = await connection.QuerySingleAsync<long>(insertDetectionSql, new
                {
                    MessageId = messageId,
                    DetectedAt = timestamp,
                    Source = source,
                    IsSpam = isSpam,
                    Confidence = confidenceWhenAdded,
                    AddedBy = addedBy
                }, transaction);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Added detection result: {Type} from {Source} (Detection ID: {Id}, Message ID: {MessageId})",
                    isSpam ? "SPAM" : "HAM", source, detectionId, messageId);

                return detectionId;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add detection result: {Text}", messageText);
            throw;
        }
    }

    /// <summary>
    /// Get detection results by source
    /// </summary>
    public async Task<IEnumerable<TrainingSample>> GetSamplesBySourceAsync(string source, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT dr.id, m.message_text, dr.is_spam, dr.detected_at as added_date,
                       dr.detection_source as source, dr.confidence as confidence_when_added,
                       ARRAY[m.chat_id] as chat_ids, dr.added_by,
                       0 as detection_count, NULL::bigint as last_detected_date
                FROM detection_results dr
                JOIN messages m ON dr.message_id = m.message_id
                WHERE dr.detection_source = @Source
                ORDER BY dr.detected_at DESC";

            var dtos = await connection.QueryAsync<TrainingSampleDto>(sql, new { Source = source });
            return dtos.Select(dto => dto.ToTrainingSample());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve detection results for source {Source}", source);
            return Enumerable.Empty<TrainingSample>();
        }
    }

    /// <summary>
    /// Delete detection results older than specified date
    /// NOTE: In normalized schema, we generally keep all detection_results for analytics
    /// This should rarely be used
    /// </summary>
    public async Task<int> DeleteOldSamplesAsync(long olderThanUnixTime, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = "DELETE FROM detection_results WHERE detected_at < @OlderThanUnixTime";
            var deletedCount = await connection.ExecuteAsync(sql, new { OlderThanUnixTime = olderThanUnixTime });

            _logger.LogWarning("Deleted {Count} old detection results (this removes training data!)", deletedCount);
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete old detection results");
            throw;
        }
    }

    /// <summary>
    /// Update a detection result and its associated message
    /// </summary>
    public async Task<bool> UpdateSampleAsync(long id, string messageText, bool isSpam, string source, int? confidenceWhenAdded = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Update detection_result
                const string updateDetectionSql = @"
                    UPDATE detection_results
                    SET is_spam = @IsSpam, detection_source = @Source, confidence = @Confidence
                    WHERE id = @Id
                    RETURNING message_id";

                var messageId = await connection.QuerySingleOrDefaultAsync<long?>(updateDetectionSql, new
                {
                    Id = id,
                    IsSpam = isSpam,
                    Source = source,
                    Confidence = confidenceWhenAdded
                }, transaction);

                if (!messageId.HasValue)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                // Update message text
                const string updateMessageSql = @"
                    UPDATE messages
                    SET message_text = @MessageText, content_hash = MD5(@MessageText)
                    WHERE message_id = @MessageId";

                await connection.ExecuteAsync(updateMessageSql, new
                {
                    MessageId = messageId.Value,
                    MessageText = messageText
                }, transaction);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Updated detection result {Id} to {Type}", id, isSpam ? "SPAM" : "HAM");
                return true;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update detection result {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Delete a specific detection result
    /// NOTE: This deletes training data - use with caution!
    /// </summary>
    public async Task<bool> DeleteSampleAsync(long id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = "DELETE FROM detection_results WHERE id = @Id";
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });

            if (rowsAffected > 0)
            {
                _logger.LogWarning("Deleted detection result {Id} (training data removed!)", id);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete detection result {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Get detection statistics
    /// </summary>
    public async Task<TrainingStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string countSql = @"
                SELECT
                    CAST(COUNT(*) AS INTEGER) as total,
                    CAST(COALESCE(SUM(CASE WHEN is_spam = true THEN 1 ELSE 0 END), 0) AS INTEGER) as spam,
                    CAST(COALESCE(SUM(CASE WHEN is_spam = false THEN 1 ELSE 0 END), 0) AS INTEGER) as ham
                FROM detection_results";

            const string sourceSql = @"
                SELECT id, detection_source as source
                FROM detection_results";

            // Use DTO to handle nullable SUM results properly
            var statsDto = await connection.QuerySingleAsync<TrainingStatsDto>(countSql);

            // Pull id+source and count in C# to avoid type issues with COUNT(*)
            var rows = await connection.QueryAsync<(long id, string source)>(sourceSql);
            var sourceDict = rows
                .GroupBy(r => r.source)
                .OrderByDescending(g => g.Count())
                .ToDictionary(
                    g => g.Key,
                    g => g.Count()
                );

            // Convert DTO to domain model with populated source dictionary
            var stats = statsDto.ToTrainingStats();
            return stats with { SamplesBySource = sourceDict };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get training statistics");
            return new TrainingStats(
                TotalSamples: 0,
                SpamSamples: 0,
                HamSamples: 0,
                SpamPercentage: 0,
                SamplesBySource: new Dictionary<string, int>()
            );
        }
    }
}
