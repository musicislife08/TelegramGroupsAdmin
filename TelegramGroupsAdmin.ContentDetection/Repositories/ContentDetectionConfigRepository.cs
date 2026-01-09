using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Mappings;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository implementation for content detection configurations.
/// Uses EF Core's OwnsOne().ToJson() for strongly-typed JSONB mapping.
/// Maps between Data layer (ContentDetectionConfigData) and Business layer (ContentDetectionConfig).
/// </summary>
public class ContentDetectionConfigRepository : IContentDetectionConfigRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ContentDetectionConfigRepository> _logger;

    public ContentDetectionConfigRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<ContentDetectionConfigRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get the global content detection configuration (chat_id = 0)
    /// </summary>
    public async Task<ContentDetectionConfig> GetGlobalConfigAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var entity = await context.ContentDetectionConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == 0)
                .OrderByDescending(c => c.LastUpdated)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity?.Config == null)
            {
                _logger.LogWarning("No global config found in database, returning defaults");
                return new ContentDetectionConfig();
            }

            return entity.Config.ToModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve global content detection configuration");
            return new ContentDetectionConfig();
        }
    }

    /// <summary>
    /// Update the global content detection configuration
    /// </summary>
    public async Task<bool> UpdateGlobalConfigAsync(ContentDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var timestamp = DateTimeOffset.UtcNow;
            var configData = config.ToData();

            var entity = await context.ContentDetectionConfigs
                .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

            if (entity == null)
            {
                entity = new TelegramGroupsAdmin.Data.Models.ContentDetectionConfigRecordDto
                {
                    ChatId = 0,
                    Config = configData,
                    LastUpdated = timestamp,
                    UpdatedBy = updatedBy
                };
                context.ContentDetectionConfigs.Add(entity);
            }
            else
            {
                entity.Config = configData;
                entity.LastUpdated = timestamp;
                entity.UpdatedBy = updatedBy;
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated global content detection configuration (updated_by: {UpdatedBy})", updatedBy);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update global content detection configuration");
            throw;
        }
    }

    /// <summary>
    /// Get the raw chat-specific configuration (without merging with global).
    /// Returns null if no chat-specific config exists.
    /// Used by UI components to preserve UseGlobal flags when editing.
    /// </summary>
    public async Task<ContentDetectionConfig?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var entity = await context.ContentDetectionConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == chatId)
                .OrderByDescending(c => c.LastUpdated)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity?.Config == null)
            {
                _logger.LogDebug("No chat-specific config found for chat {ChatId}", chatId);
                return null;
            }

            _logger.LogDebug("Loaded raw chat config for {ChatId}", chatId);
            return entity.Config.ToModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve chat config for chat {ChatId}", chatId);
            return null;
        }
    }

    /// <summary>
    /// Get effective configuration for a specific chat with section-by-section fallback to global config.
    /// Supports granular overrides via UseGlobal flags in each sub-config.
    /// </summary>
    public async Task<ContentDetectionConfig> GetEffectiveConfigAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var entities = await context.ContentDetectionConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == 0 || c.ChatId == chatId)
                .OrderByDescending(c => c.LastUpdated)
                .ToListAsync(cancellationToken);

            var globalEntity = entities.FirstOrDefault(e => e.ChatId == 0);
            var chatEntity = entities.FirstOrDefault(e => e.ChatId == chatId);

            if (globalEntity?.Config == null)
            {
                _logger.LogWarning("No global config found, returning defaults");
                return new ContentDetectionConfig();
            }

            var globalConfig = globalEntity.Config.ToModel();

            if (chatEntity?.Config == null)
            {
                _logger.LogDebug("No chat-specific config for {ChatId}, using global", chatId);
                return globalConfig;
            }

            var chatConfig = chatEntity.Config.ToModel();

            // Merge section-by-section based on UseGlobal flags
            var merged = new ContentDetectionConfig
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
                AIVeto = chatConfig.AIVeto.UseGlobal ? globalConfig.AIVeto : chatConfig.AIVeto,
                UrlBlocklist = chatConfig.UrlBlocklist.UseGlobal ? globalConfig.UrlBlocklist : chatConfig.UrlBlocklist,
                ThreatIntel = chatConfig.ThreatIntel.UseGlobal ? globalConfig.ThreatIntel : chatConfig.ThreatIntel,
                SeoScraping = chatConfig.SeoScraping.UseGlobal ? globalConfig.SeoScraping : chatConfig.SeoScraping,
                ImageSpam = chatConfig.ImageSpam.UseGlobal ? globalConfig.ImageSpam : chatConfig.ImageSpam,
                VideoSpam = chatConfig.VideoSpam.UseGlobal ? globalConfig.VideoSpam : chatConfig.VideoSpam,
                FileScanning = chatConfig.FileScanning.UseGlobal ? globalConfig.FileScanning : chatConfig.FileScanning
            };

            _logger.LogDebug("Loaded merged config for chat {ChatId} (global + chat overrides)", chatId);
            return merged;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve content detection configuration for chat {ChatId}", chatId);
            return new ContentDetectionConfig();
        }
    }

    /// <summary>
    /// Update configuration for a specific chat
    /// </summary>
    public async Task<bool> UpdateChatConfigAsync(long chatId, ContentDetectionConfig config, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var timestamp = DateTimeOffset.UtcNow;
            var configData = config.ToData();

            var entity = await context.ContentDetectionConfigs
                .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

            if (entity == null)
            {
                entity = new TelegramGroupsAdmin.Data.Models.ContentDetectionConfigRecordDto
                {
                    ChatId = chatId,
                    Config = configData,
                    LastUpdated = timestamp,
                    UpdatedBy = updatedBy
                };
                context.ContentDetectionConfigs.Add(entity);
            }
            else
            {
                entity.Config = configData;
                entity.LastUpdated = timestamp;
                entity.UpdatedBy = updatedBy;
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated content detection configuration for chat {ChatId}", chatId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update content detection configuration for chat {ChatId}", chatId);
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
            var entities = await context.ContentDetectionConfigs
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
            var entity = await context.ContentDetectionConfigs
                .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

            if (entity == null)
                return false;

            context.ContentDetectionConfigs.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted content detection configuration for chat {ChatId}", chatId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete content detection configuration for chat {ChatId}", chatId);
            throw;
        }
    }

    /// <summary>
    /// Get the names of checks that have AlwaysRun=true for the given chat.
    /// Uses optimized raw SQL to query JSONB directly, handling UseGlobal merging.
    /// Returns check names that are both Enabled and AlwaysRun.
    /// </summary>
    public async Task<HashSet<string>> GetCriticalCheckNamesAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Raw SQL query that:
        // 1. Extracts sub-configs from JSONB using jsonb_each
        // 2. Gets both global (chat_id=0) and chat-specific configs
        // 3. For each check, uses chat-specific value unless useGlobal=true
        // 4. Returns check names where enabled=true AND alwaysRun=true
        const string sql = """
            WITH configs AS (
              SELECT
                chat_id,
                key,
                value
              FROM content_detection_configs,
              LATERAL jsonb_each(config_json) AS items(key, value)
              WHERE chat_id = {0} OR chat_id = 0
            ),
            effective_configs AS (
              SELECT
                c.key,
                CASE
                  WHEN chat.value IS NOT NULL
                       AND COALESCE((chat.value->>'UseGlobal')::boolean, false) = false
                    THEN chat.value
                  ELSE global.value
                END as value
              FROM (SELECT DISTINCT key FROM configs) c
              LEFT JOIN (SELECT key, value FROM configs WHERE chat_id = {0}) chat ON c.key = chat.key
              LEFT JOIN (SELECT key, value FROM configs WHERE chat_id = 0) global ON c.key = global.key
            )
            SELECT key as "Value"
            FROM effective_configs
            WHERE value IS NOT NULL
              AND COALESCE((value->>'Enabled')::boolean, false) = true
              AND COALESCE((value->>'AlwaysRun')::boolean, false) = true
            """;

        try
        {
            var results = await context.Database
                .SqlQueryRaw<string>(sql, chatId)
                .ToListAsync(cancellationToken);

            return results.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get critical check names for chat {ChatId}", chatId);
            return [];
        }
    }
}
