using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.SpamDetection.Configuration;

namespace TelegramGroupsAdmin.SpamDetection.Repositories;

/// <summary>
/// Repository implementation for spam detection configurations
/// </summary>
public class SpamDetectionConfigRepository : ISpamDetectionConfigRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<SpamDetectionConfigRepository> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,  // Important for deserialization!
        WriteIndented = true
    };

    public SpamDetectionConfigRepository(AppDbContext context, ILogger<SpamDetectionConfigRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get the global spam detection configuration (chat_id = '0')
    /// </summary>
    public async Task<SpamDetectionConfig> GetGlobalConfigAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _context.SpamDetectionConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == "0")
                .OrderByDescending(c => c.LastUpdated)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity == null || string.IsNullOrEmpty(entity.ConfigJson))
            {
                // Return default configuration if none exists
                _logger.LogWarning("No global config found in database, returning defaults");
                return new SpamDetectionConfig();
            }

            var config = JsonSerializer.Deserialize<SpamDetectionConfig>(entity.ConfigJson, JsonOptions);

            // Debug logging to verify what we loaded
            _logger.LogDebug("Loaded global config - StopWords.Enabled: {StopWordsEnabled}, CAS.Enabled: {CasEnabled}, Bayes.Enabled: {BayesEnabled}",
                config?.StopWords.Enabled, config?.Cas.Enabled, config?.Bayes.Enabled);

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
            var configJson = JsonSerializer.Serialize(config, JsonOptions);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Debug logging to verify what we're saving
            _logger.LogDebug("Saving global config - StopWords.Enabled: {StopWordsEnabled}, CAS.Enabled: {CasEnabled}, Bayes.Enabled: {BayesEnabled}",
                config.StopWords.Enabled, config.Cas.Enabled, config.Bayes.Enabled);

            var entity = await _context.SpamDetectionConfigs
                .FirstOrDefaultAsync(c => c.ChatId == "0", cancellationToken);

            if (entity == null)
            {
                // Insert new record
                entity = new TelegramGroupsAdmin.Data.Models.SpamDetectionConfigRecord
                {
                    ChatId = "0",
                    ConfigJson = configJson,
                    LastUpdated = timestamp,
                    UpdatedBy = updatedBy
                };
                _context.SpamDetectionConfigs.Add(entity);
            }
            else
            {
                // Update existing record
                entity.ConfigJson = configJson;
                entity.LastUpdated = timestamp;
                entity.UpdatedBy = updatedBy;
            }

            await _context.SaveChangesAsync(cancellationToken);

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
    /// Get configuration for a specific chat (falls back to global if not found)
    /// Uses a single SQL query with COALESCE to check chat-specific config first, then global
    /// </summary>
    public async Task<SpamDetectionConfig> GetChatConfigAsync(string chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get chat-specific config first
            var chatEntity = await _context.SpamDetectionConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

            string? configJson = chatEntity?.ConfigJson;

            // If no chat-specific config, fall back to global
            if (string.IsNullOrEmpty(configJson))
            {
                var globalEntity = await _context.SpamDetectionConfigs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.ChatId == "0", cancellationToken);

                configJson = globalEntity?.ConfigJson;
            }

            if (string.IsNullOrEmpty(configJson))
            {
                // No config found at all (neither chat-specific nor global)
                _logger.LogWarning("No config found for chat {ChatId} or global, returning defaults", chatId);
                return new SpamDetectionConfig();
            }

            var config = JsonSerializer.Deserialize<SpamDetectionConfig>(configJson, JsonOptions);

            _logger.LogDebug("Loaded config for chat {ChatId}", chatId);

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
    public async Task<bool> UpdateChatConfigAsync(string chatId, SpamDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var configJson = JsonSerializer.Serialize(config, JsonOptions);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var entity = await _context.SpamDetectionConfigs
                .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

            if (entity == null)
            {
                // Insert new record
                entity = new TelegramGroupsAdmin.Data.Models.SpamDetectionConfigRecord
                {
                    ChatId = chatId,
                    ConfigJson = configJson,
                    LastUpdated = timestamp,
                    UpdatedBy = updatedBy
                };
                _context.SpamDetectionConfigs.Add(entity);
            }
            else
            {
                // Update existing record
                entity.ConfigJson = configJson;
                entity.LastUpdated = timestamp;
                entity.UpdatedBy = updatedBy;
            }

            await _context.SaveChangesAsync(cancellationToken);

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
            var entities = await _context.SpamDetectionConfigs
                .AsNoTracking()
                .OrderBy(c => c.ChatId == "0" ? 0 : 1)
                .ThenBy(c => c.ChatId)
                .ToListAsync(cancellationToken);

            return entities.Select(e => new ChatConfigInfo
            {
                ChatId = e.ChatId ?? "0",
                LastUpdated = e.LastUpdated,
                UpdatedBy = e.UpdatedBy,
                ChatName = e.ChatId == "0" ? "Global Configuration" : e.ChatId,
                HasCustomConfig = e.ChatId != "0"
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
    public async Task<bool> DeleteChatConfigAsync(string chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _context.SpamDetectionConfigs
                .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

            if (entity == null)
                return false;

            _context.SpamDetectionConfigs.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);

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
