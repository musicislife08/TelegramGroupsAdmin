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
    /// Get the raw chat-specific configuration (without merging with global)
    /// Returns null if no chat-specific config exists
    /// Used by UI components to preserve UseGlobal flags when editing
    /// </summary>
    public async Task<SpamDetectionConfig?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var entity = await context.SpamDetectionConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == chatId)
                .OrderByDescending(c => c.LastUpdated)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity == null || string.IsNullOrEmpty(entity.ConfigJson))
            {
                _logger.LogDebug("No chat-specific config found for chat {ChatId}", chatId);
                return null;
            }

            var config = JsonSerializer.Deserialize<SpamDetectionConfig>(entity.ConfigJson, JsonOptions);
            _logger.LogDebug("Loaded raw chat config for {ChatId}", chatId);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve chat config for chat {ChatId}", chatId);
            return null; // Return null on error
        }
    }

    /// <summary>
    /// Get effective configuration for a specific chat with section-by-section fallback to global config
    /// NEW: Supports granular overrides via UseGlobal flags in each sub-config
    /// Loads both global and chat configs, then merges section-by-section based on UseGlobal flags
    /// </summary>
    public async Task<SpamDetectionConfig> GetEffectiveConfigAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            // Load both global (chat_id=0) and chat-specific configs
            var entities = await context.SpamDetectionConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == 0 || c.ChatId == chatId)
                .OrderByDescending(c => c.LastUpdated)  // Most recent first
                .ToListAsync(cancellationToken);

            var globalEntity = entities.FirstOrDefault(e => e.ChatId == 0);
            var chatEntity = entities.FirstOrDefault(e => e.ChatId == chatId);

            // If no global config exists, return defaults
            if (globalEntity == null || string.IsNullOrEmpty(globalEntity.ConfigJson))
            {
                _logger.LogWarning("No global config found, returning defaults");
                return new SpamDetectionConfig();
            }

            var globalConfig = JsonSerializer.Deserialize<SpamDetectionConfig>(globalEntity.ConfigJson, JsonOptions) ?? new SpamDetectionConfig();

            // If no chat-specific config, return global as-is
            if (chatEntity == null || string.IsNullOrEmpty(chatEntity.ConfigJson))
            {
                _logger.LogDebug("No chat-specific config for {ChatId}, using global", chatId);
                return globalConfig;
            }

            var chatConfig = JsonSerializer.Deserialize<SpamDetectionConfig>(chatEntity.ConfigJson, JsonOptions) ?? new SpamDetectionConfig();

            // Merge section-by-section based on UseGlobal flags
            var merged = new SpamDetectionConfig
            {
                // Top-level properties - always from chat config when it exists
                FirstMessageOnly = chatConfig.FirstMessageOnly,
                FirstMessagesCount = chatConfig.FirstMessagesCount,
                MinMessageLength = chatConfig.MinMessageLength,
                AutoBanThreshold = chatConfig.AutoBanThreshold,
                ReviewQueueThreshold = chatConfig.ReviewQueueThreshold,
                MaxConfidenceVetoThreshold = chatConfig.MaxConfidenceVetoThreshold,
                TrainingMode = chatConfig.TrainingMode,

                // Section-by-section merge based on UseGlobal flags
                StopWords = chatConfig.StopWords.UseGlobal ? globalConfig.StopWords : chatConfig.StopWords,
                Similarity = chatConfig.Similarity.UseGlobal ? globalConfig.Similarity : chatConfig.Similarity,
                Cas = chatConfig.Cas.UseGlobal ? globalConfig.Cas : chatConfig.Cas,
                Bayes = chatConfig.Bayes.UseGlobal ? globalConfig.Bayes : chatConfig.Bayes,
                InvisibleChars = chatConfig.InvisibleChars.UseGlobal ? globalConfig.InvisibleChars : chatConfig.InvisibleChars,
                Translation = chatConfig.Translation.UseGlobal ? globalConfig.Translation : chatConfig.Translation,
                Spacing = chatConfig.Spacing.UseGlobal ? globalConfig.Spacing : chatConfig.Spacing,
                OpenAI = chatConfig.OpenAI.UseGlobal ? globalConfig.OpenAI : chatConfig.OpenAI,
                UrlBlocklist = chatConfig.UrlBlocklist.UseGlobal ? globalConfig.UrlBlocklist : chatConfig.UrlBlocklist,
                ThreatIntel = chatConfig.ThreatIntel.UseGlobal ? globalConfig.ThreatIntel : chatConfig.ThreatIntel,
                SeoScraping = chatConfig.SeoScraping.UseGlobal ? globalConfig.SeoScraping : chatConfig.SeoScraping,
                ImageSpam = chatConfig.ImageSpam.UseGlobal ? globalConfig.ImageSpam : chatConfig.ImageSpam
            };

            _logger.LogDebug("Loaded merged config for chat {ChatId} (global + chat overrides)", chatId);

            return merged;
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
            return [];
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
