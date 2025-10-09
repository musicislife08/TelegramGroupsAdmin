using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace TelegramGroupsAdmin.SpamDetection.Repositories;

/// <summary>
/// Repository implementation for stop words management
/// </summary>
public class StopWordsRepository : IStopWordsRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<StopWordsRepository> _logger;

    public StopWordsRepository(NpgsqlDataSource dataSource, ILogger<StopWordsRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Get all enabled stop words
    /// </summary>
    public async Task<IEnumerable<string>> GetEnabledStopWordsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = "SELECT word FROM stop_words WHERE enabled = true ORDER BY word";
            var words = await connection.QueryAsync<string>(sql);
            return words;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve enabled stop words");
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Add a new stop word
    /// </summary>
    public async Task<long> AddStopWordAsync(string word, string? addedBy = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = @"
                INSERT INTO stop_words (word, enabled, added_date, added_by, notes)
                VALUES (@Word, true, @AddedDate, @AddedBy, @Notes)
                RETURNING id";

            var id = await connection.QuerySingleAsync<long>(sql, new
            {
                Word = word.ToLowerInvariant(),
                AddedDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                AddedBy = addedBy,
                Notes = notes
            });

            _logger.LogInformation("Added stop word: {Word} (ID: {Id})", word, id);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add stop word: {Word}", word);
            throw;
        }
    }

    /// <summary>
    /// Enable or disable a stop word
    /// </summary>
    public async Task<bool> SetStopWordEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = "UPDATE stop_words SET enabled = @Enabled WHERE id = @Id";
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id, Enabled = enabled });

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Updated stop word {Id} enabled status to {Enabled}", id, enabled);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update stop word {Id} enabled status", id);
            throw;
        }
    }

    /// <summary>
    /// Check if a word exists as a stop word
    /// </summary>
    public async Task<bool> ExistsAsync(string word, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = "SELECT COUNT(*) FROM stop_words WHERE word = @Word";
            var count = await connection.QuerySingleAsync<int>(sql, new { Word = word.ToLowerInvariant() });
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if stop word exists: {Word}", word);
            return false;
        }
    }

    /// <summary>
    /// Get all stop words (enabled and disabled) with full details
    /// </summary>
    public async Task<IEnumerable<object>> GetAllStopWordsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT s.id, s.word, s.enabled, s.added_date,
                       COALESCE(u.email, 'Unknown') as added_by,
                       s.notes
                FROM stop_words s
                LEFT JOIN users u ON s.added_by = u.id
                ORDER BY s.word";

            var stopWords = await connection.QueryAsync(sql);
            return stopWords;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all stop words");
            return Enumerable.Empty<object>();
        }
    }

    /// <summary>
    /// Update stop word notes
    /// </summary>
    public async Task<bool> UpdateStopWordNotesAsync(long id, string? notes, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = "UPDATE stop_words SET notes = @Notes WHERE id = @Id";
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id, Notes = notes });

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Updated stop word {Id} notes", id);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update stop word {Id} notes", id);
            throw;
        }
    }
}