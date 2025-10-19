using TelegramGroupsAdmin.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.ContentDetection.Configuration;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository implementation for spam detection configurations
/// Uses DbContextFactory to avoid concurrency issues
/// </summary>
public class SpamDetectionConfigRepository : ISpamDetectionConfigRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<SpamDetectionConfigRepository> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,  // Important for deserialization!
        WriteIndented = true
    };

    public SpamDetectionConfigRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<SpamDetectionConfigRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get the global spam detection configuration (chat_id = 0)
    /// </summary>
    public async Task<SpamDetectionConfig> GetGlobalConfigAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var entity = await context.SpamDetectionConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == 0)
                .OrderByDescending(c => c.LastUpdated)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity == null || string.IsNullOrEmpty(entity.ConfigJson))
            {
                // Return default configuration if none exists
                _logger.LogWarning("No global config found in database, returning defaults");
                return new SpamDetectionConfig();
            }

            var config = JsonSerializer.Deserialize<SpamDetectionConfig>(entity.ConfigJson, JsonOptions);
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
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var configJson = JsonSerializer.Serialize(config, JsonOptions);
            var timestamp = DateTimeOffset.UtcNow;

            var entity = await context.SpamDetectionConfigs
                .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

            if (entity == null)
            {
                // Insert new record
                entity = new TelegramGroupsAdmin.Data.Models.SpamDetectionConfigRecordDto
                {
                    ChatId = 0,
                    ConfigJson = configJson,
                    LastUpdated = timestamp,
                    UpdatedBy = updatedBy
                };
                context.SpamDetectionConfigs.Add(entity);
            }
            else
            {
                // Update existing record
                entity.ConfigJson = configJson;
                entity.LastUpdated = timestamp;
                entity.UpdatedBy = updatedBy;
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated global spam detection configuration (updated_by: {UpdatedBy})", updatedBy);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update global spam detection configuration");
            throw;
        }
    }

    /// <summary>
    /// Get effective configuration for a specific chat (chat-specific overrides, falls back to global defaults)
    /// Uses a SINGLE SQL query that returns chat-specific config if exists, otherwise global config
    /// SQL: WHERE chat_id IN ({chatId}, 0) ORDER BY chat_id = {chatId} DESC, last_updated DESC LIMIT 1
    /// </summary>
    public async Task<SpamDetectionConfig> GetEffectiveConfigAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            // Single query: try chat-specific first, then global, using ORDER BY to prioritize
            // This generates: WHERE chat_id IN ({chatId}, 0) ORDER BY (CASE WHEN chat_id = {chatId} THEN 0 ELSE 1 END), last_updated DESC LIMIT 1
            var entity = await context.SpamDetectionConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == chatId || c.ChatId == 0)
                .OrderBy(c => c.ChatId == chatId ? 0 : 1)  // Chat-specific (0) comes before global (1)
                .ThenByDescending(c => c.LastUpdated)      // Most recent if multiple entries
                .FirstOrDefaultAsync(cancellationToken);

            if (entity == null || string.IsNullOrEmpty(entity.ConfigJson))
            {
                // No config found at all (neither chat-specific nor global)
                _logger.LogWarning("No config found for chat {ChatId} or global, returning defaults", chatId);
                return new SpamDetectionConfig();
            }

            var config = JsonSerializer.Deserialize<SpamDetectionConfig>(entity.ConfigJson, JsonOptions);

            _logger.LogDebug("Loaded effective config for chat {ChatId} (source: {SourceChatId})",
                chatId, entity.ChatId);

            return config ?? new SpamDetectionConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve spam detection configuration for chat {ChatId}", chatId);
            return new SpamDetectionConfig(); // Return default on error
        }
    }

    /// <summary>
    /// Update configuration for a specific chat
    /// </summary>
    public async Task<bool> UpdateChatConfigAsync(long chatId, SpamDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var configJson = JsonSerializer.Serialize(config, JsonOptions);
            var timestamp = DateTimeOffset.UtcNow;

            var entity = await context.SpamDetectionConfigs
                .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

            if (entity == null)
            {
                // Insert new record
                entity = new TelegramGroupsAdmin.Data.Models.SpamDetectionConfigRecordDto
                {
                    ChatId = chatId,
                    ConfigJson = configJson,
                    LastUpdated = timestamp,
                    UpdatedBy = updatedBy
                };
                context.SpamDetectionConfigs.Add(entity);
            }
            else
            {
                // Update existing record
                entity.ConfigJson = configJson;
                entity.LastUpdated = timestamp;
                entity.UpdatedBy = updatedBy;
            }

            await context.SaveChangesAsync(cancellationToken);

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
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var entities = await context.SpamDetectionConfigs
                .AsNoTracking()
                .OrderBy(c => c.ChatId == 0 ? 0 : 1)
                .ThenBy(c => c.ChatId)
                .ToListAsync(cancellationToken);

            return entities.Select(e => new ChatConfigInfo
            {
                ChatId = e.ChatId ?? 0,
                LastUpdated = e.LastUpdated,
                UpdatedBy = e.UpdatedBy,
                ChatName = e.ChatId == 0 ? "Global Configuration" : e.ChatId.ToString(),
                HasCustomConfig = e.ChatId != 0
            });
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
    public async Task<bool> DeleteChatConfigAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var entity = await context.SpamDetectionConfigs
                .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

            if (entity == null)
                return false;

            context.SpamDetectionConfigs.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted spam detection configuration for chat {ChatId}", chatId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete spam detection configuration for chat {ChatId}", chatId);
            throw;
        }
    }
}
