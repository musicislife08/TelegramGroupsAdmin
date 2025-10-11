using Npgsql;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Configuration;

namespace TelegramGroupsAdmin.SpamDetection.Repositories;

/// <summary>
/// Repository implementation for spam detection configurations
/// </summary>
public class SpamDetectionConfigRepository : ISpamDetectionConfigRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<SpamDetectionConfigRepository> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public SpamDetectionConfigRepository(NpgsqlDataSource dataSource, ILogger<SpamDetectionConfigRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Get the global spam detection configuration
    /// </summary>
    public async Task<SpamDetectionConfig> GetGlobalConfigAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT config_json
                FROM spam_detection_configs
                WHERE chat_id IS NULL
                ORDER BY last_updated DESC
                LIMIT 1";

            var configJson = await connection.QuerySingleOrDefaultAsync<string>(sql);

            if (string.IsNullOrEmpty(configJson))
            {
                // Return default configuration if none exists
                return new SpamDetectionConfig();
            }

            var config = JsonSerializer.Deserialize<SpamDetectionConfig>(configJson, JsonOptions);
            return config ?? new SpamDetectionConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve global spam detection configuration");
            return new SpamDetectionConfig(); // Return default on error
        }
    }

    /// <summary>
    /// Update the global spam detection configuration
    /// </summary>
    public async Task<bool> UpdateGlobalConfigAsync(SpamDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var configJson = JsonSerializer.Serialize(config, JsonOptions);

            // Check if global config exists (chat_id IS NULL)
            const string checkSql = "SELECT COUNT(*) FROM spam_detection_configs WHERE chat_id IS NULL";
            var exists = await connection.ExecuteScalarAsync<int>(checkSql) > 0;

            if (exists)
            {
                // Update existing global config
                const string updateSql = @"
                    UPDATE spam_detection_configs
                    SET config_json = @ConfigJson,
                        last_updated = @LastUpdated,
                        updated_by = @UpdatedBy
                    WHERE chat_id IS NULL";

                await connection.ExecuteAsync(updateSql, new
                {
                    ConfigJson = configJson,
                    LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    UpdatedBy = updatedBy
                });
            }
            else
            {
                // Insert new global config
                const string insertSql = @"
                    INSERT INTO spam_detection_configs (chat_id, config_json, last_updated, updated_by)
                    VALUES (NULL, @ConfigJson, @LastUpdated, @UpdatedBy)";

                await connection.ExecuteAsync(insertSql, new
                {
                    ConfigJson = configJson,
                    LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    UpdatedBy = updatedBy
                });
            }

            _logger.LogInformation("Updated global spam detection configuration");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update global spam detection configuration");
            throw;
        }
    }

    /// <summary>
    /// Get configuration for a specific chat (falls back to global if not found)
    /// </summary>
    public async Task<SpamDetectionConfig> GetChatConfigAsync(string chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT config_json
                FROM spam_detection_configs
                WHERE chat_id = @ChatId
                ORDER BY last_updated DESC
                LIMIT 1";

            var configJson = await connection.QuerySingleOrDefaultAsync<string>(sql, new { ChatId = chatId });

            if (string.IsNullOrEmpty(configJson))
            {
                // Fall back to global configuration
                return await GetGlobalConfigAsync(cancellationToken);
            }

            var config = JsonSerializer.Deserialize<SpamDetectionConfig>(configJson, JsonOptions);
            return config ?? await GetGlobalConfigAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve spam detection configuration for chat {ChatId}", chatId);
            return await GetGlobalConfigAsync(cancellationToken); // Fall back to global
        }
    }

    /// <summary>
    /// Update configuration for a specific chat
    /// </summary>
    public async Task<bool> UpdateChatConfigAsync(string chatId, SpamDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var configJson = JsonSerializer.Serialize(config, JsonOptions);

            // PostgreSQL uses ON CONFLICT instead of INSERT OR REPLACE
            const string sql = @"
                INSERT INTO spam_detection_configs (chat_id, config_json, last_updated, updated_by)
                VALUES (@ChatId, @ConfigJson, @LastUpdated, @UpdatedBy)
                ON CONFLICT (chat_id) DO UPDATE
                SET config_json = EXCLUDED.config_json,
                    last_updated = EXCLUDED.last_updated,
                    updated_by = EXCLUDED.updated_by";

            await connection.ExecuteAsync(sql, new
            {
                ChatId = chatId,
                ConfigJson = configJson,
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UpdatedBy = updatedBy
            });

            _logger.LogInformation("Updated spam detection configuration for chat {ChatId}", chatId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update spam detection configuration for chat {ChatId}", chatId);
            throw;
        }
    }

    /// <summary>
    /// Get all configured chats
    /// </summary>
    public async Task<IEnumerable<ChatConfigInfo>> GetAllChatConfigsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT
                    chat_id as ChatId,
                    last_updated as LastUpdated,
                    updated_by as UpdatedBy,
                    CASE WHEN chat_id IS NULL THEN 'Global Configuration' ELSE chat_id END as ChatName,
                    CASE WHEN chat_id IS NOT NULL THEN 1 ELSE 0 END as HasCustomConfig
                FROM spam_detection_configs
                ORDER BY
                    CASE WHEN chat_id IS NULL THEN 0 ELSE 1 END,
                    chat_id";

            var configs = await connection.QueryAsync<ChatConfigInfo>(sql);
            return configs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve chat configurations");
            return Enumerable.Empty<ChatConfigInfo>();
        }
    }

    /// <summary>
    /// Delete configuration for a specific chat (falls back to global)
    /// </summary>
    public async Task<bool> DeleteChatConfigAsync(string chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = "DELETE FROM spam_detection_configs WHERE chat_id = @ChatId";
            var rowsAffected = await connection.ExecuteAsync(sql, new { ChatId = chatId });

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Deleted spam detection configuration for chat {ChatId}", chatId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete spam detection configuration for chat {ChatId}", chatId);
            throw;
        }
    }
}
