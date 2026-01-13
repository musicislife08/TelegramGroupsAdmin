using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// Service for managing unified configuration storage with automatic global/chat merging
/// PERF-CFG-1: Uses HybridCache for 95% query reduction with stampede protection
/// Routes ConfigType.ContentDetection to IContentDetectionConfigRepository (separate table)
/// </summary>
public class ConfigService(
    IConfigRepository configRepository,
    IContentDetectionConfigRepository contentDetectionConfigRepository,
    HybridCache cache,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ConfigService> logger) : IConfigService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private static readonly HybridCacheEntryOptions CacheOptions = new() { Expiration = CacheDuration };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task SaveAsync<T>(ConfigType configType, long chatId, T config) where T : class
    {
        ArgumentNullException.ThrowIfNull(config);

        // Route ContentDetection to separate repository
        if (configType == ConfigType.ContentDetection)
        {
            if (config is not ContentDetectionConfig cdConfig)
                throw new ArgumentException($"Expected ContentDetectionConfig but got {typeof(T).Name}", nameof(config));

            if (chatId == 0)
                await contentDetectionConfigRepository.UpdateGlobalConfigAsync(cdConfig);
            else
                await contentDetectionConfigRepository.UpdateChatConfigAsync(chatId, cdConfig);

            logger.LogInformation("Configuration saved: {ConfigType} ({Scope})", configType, chatId == 0 ? "global" : $"chat {chatId}");

            // Invalidate cache
            await cache.RemoveAsync($"cfg_{configType}_{chatId}");
            if (chatId != 0)
                await cache.RemoveAsync($"cfg_effective_{configType}_{chatId}");
            return;
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var record = await configRepository.GetAsync(chatId);

        if (record == null)
        {
            record = new ConfigRecordDto
            {
                ChatId = chatId
                // CreatedAt will be set by database default (NOW())
            };
        }

        // Set the appropriate config column based on config type
        SetConfigColumn(record, configType, json);

        await configRepository.UpsertAsync(record);

        var scope = chatId == 0 ? "global" : $"chat {chatId}";
        logger.LogInformation("Configuration saved: {ConfigType} ({Scope})", configType, scope);

        // CRITICAL: Invalidate cache immediately for instant UI updates
        var cacheKey = $"cfg_{configType}_{chatId}";
        await cache.RemoveAsync(cacheKey);

        // Also invalidate effective config cache if chat-specific
        if (chatId != 0)
        {
            var effectiveCacheKey = $"cfg_effective_{configType}_{chatId}";
            await cache.RemoveAsync(effectiveCacheKey);
        }

        // If updating global config, invalidate all chat-specific effective caches
        // (they fall back to global, so need to pick up new global values)
        if (chatId == 0)
        {
            // Note: We can't easily enumerate all chat IDs here, but the 15-min expiration
            // ensures eventual consistency. For instant updates, users can refresh the page.
            // Alternative: Keep a registry of cache keys, but adds complexity.
        }
    }

    public async ValueTask<T?> GetAsync<T>(ConfigType configType, long chatId) where T : class
    {
        var cacheKey = $"cfg_{configType}_{chatId}";

        // Route ContentDetection to separate repository
        if (configType == ConfigType.ContentDetection)
        {
            // HybridCache provides stampede protection - only one caller fetches on miss
            var cdConfig = await cache.GetOrCreateAsync(
                cacheKey,
                async _ =>
                {
                    if (chatId == 0)
                        return await contentDetectionConfigRepository.GetGlobalConfigAsync();
                    return await contentDetectionConfigRepository.GetByChatIdAsync(chatId);
                },
                CacheOptions);

            return cdConfig as T;
        }

        // Regular configs - use GetOrCreateAsync for cache-aside with stampede protection
        return await cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var record = await configRepository.GetAsync(chatId);
                if (record == null)
                    return null;

                var json = GetConfigColumn(record, configType);
                if (string.IsNullOrEmpty(json))
                    return null;

                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            },
            CacheOptions);
    }

    public async ValueTask<T?> GetEffectiveAsync<T>(ConfigType configType, long chatId) where T : class
    {
        // Route ContentDetection to separate repository (has its own merge logic)
        if (configType == ConfigType.ContentDetection)
        {
            var cdCacheKey = $"cfg_effective_{configType}_{chatId}";
            var cdConfig = await cache.GetOrCreateAsync(
                cdCacheKey,
                async _ => await contentDetectionConfigRepository.GetEffectiveConfigAsync(chatId),
                CacheOptions);

            return cdConfig as T;
        }

        // If requesting global config, just return it directly (uses cached GetAsync)
        if (chatId == 0)
        {
            return await GetAsync<T>(configType, 0);
        }

        // Cache the effective (merged) config with stampede protection
        var effectiveCacheKey = $"cfg_effective_{configType}_{chatId}";

        return await cache.GetOrCreateAsync(
            effectiveCacheKey,
            async _ =>
            {
                // Get both global and chat-specific configs (both use cached GetAsync)
                var globalConfig = await GetAsync<T>(configType, 0);
                var chatConfig = await GetAsync<T>(configType, chatId);

                // If no chat-specific config, return global
                if (chatConfig == null)
                    return globalConfig;

                // If no global config, return chat-specific
                if (globalConfig == null)
                    return chatConfig;

                // Merge: chat-specific overrides global
                return MergeConfigs(globalConfig, chatConfig);
            },
            CacheOptions);
    }

    public async Task DeleteAsync(ConfigType configType, long chatId)
    {
        // Route ContentDetection to separate repository
        if (configType == ConfigType.ContentDetection)
        {
            if (chatId != 0)
                await contentDetectionConfigRepository.DeleteChatConfigAsync(chatId);
            // Note: Can't delete global config (chat_id=0), it's the fallback

            await cache.RemoveAsync($"cfg_{configType}_{chatId}");
            await cache.RemoveAsync($"cfg_effective_{configType}_{chatId}");
            return;
        }

        var record = await configRepository.GetAsync(chatId);
        if (record == null)
        {
            return;
        }

        // Clear the specific config column
        SetConfigColumn(record, configType, null);

        // If all config columns are null, delete the entire row
        if (IsRecordEmpty(record))
        {
            await configRepository.DeleteAsync(chatId);
        }
        else
        {
            await configRepository.UpsertAsync(record);
        }

        // Invalidate cache after deletion
        var cacheKey = $"cfg_{configType}_{chatId}";
        await cache.RemoveAsync(cacheKey);

        // Also invalidate effective config cache if chat-specific
        if (chatId != 0)
        {
            var effectiveCacheKey = $"cfg_effective_{configType}_{chatId}";
            await cache.RemoveAsync(effectiveCacheKey);
        }
    }

    /// <summary>
    /// Get the encrypted Telegram bot token from database (global config only, chat_id = 0)
    /// Returns decrypted token or null if not configured
    /// </summary>
    public async ValueTask<string?> GetTelegramBotTokenAsync()
    {
        const string cacheKey = "cfg_telegram_bot_token";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                // Load from database (global config, chat_id = 0)
                var record = await configRepository.GetAsync(0);
                if (record?.TelegramBotTokenEncrypted == null)
                    return null;

                try
                {
                    // Decrypt using Data Protection
                    var protector = dataProtectionProvider.CreateProtector(DataProtectionPurposes.TelegramBotToken);
                    return protector.Unprotect(record.TelegramBotTokenEncrypted);
                }
                catch (Exception)
                {
                    // Decryption failed (corrupted data or wrong key)
                    return null;
                }
            },
            CacheOptions);
    }

    /// <summary>
    /// Save the Telegram bot token to database (encrypted, global config only, chat_id = 0)
    /// </summary>
    public async Task SaveTelegramBotTokenAsync(string botToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botToken);

        // Encrypt using Data Protection
        var protector = dataProtectionProvider.CreateProtector(DataProtectionPurposes.TelegramBotToken);
        var encryptedToken = protector.Protect(botToken);

        // Get or create global config record (chat_id = 0)
        var record = await configRepository.GetAsync(0);
        if (record == null)
        {
            record = new ConfigRecordDto
            {
                ChatId = 0
                // CreatedAt will be set by database default (NOW())
            };
        }

        // Set encrypted token
        record.TelegramBotTokenEncrypted = encryptedToken;

        await configRepository.UpsertAsync(record);

        // Invalidate cache
        const string cacheKey = "cfg_telegram_bot_token";
        await cache.RemoveAsync(cacheKey);
    }

    /// <summary>
    /// Get all content detection chat configurations (for admin UI listing).
    /// Delegates to the ContentDetection repository since it's stored in separate table.
    /// </summary>
    public async Task<IEnumerable<ChatConfigInfo>> GetAllContentDetectionConfigsAsync(CancellationToken cancellationToken = default)
    {
        return await contentDetectionConfigRepository.GetAllChatConfigsAsync(cancellationToken);
    }

    /// <summary>
    /// Get the names of content detection checks that have AlwaysRun=true for the given chat.
    /// Uses optimized JSONB query to efficiently extract only critical check names.
    /// </summary>
    public async Task<HashSet<string>> GetCriticalCheckNamesAsync(long chatId, CancellationToken cancellationToken = default)
    {
        return await contentDetectionConfigRepository.GetCriticalCheckNamesAsync(chatId, cancellationToken);
    }

    private static void SetConfigColumn(ConfigRecordDto record, ConfigType configType, string? json)
    {
        switch (configType)
        {
            case ConfigType.ContentDetection:
                // ContentDetection uses separate table via IContentDetectionConfigRepository
                throw new InvalidOperationException(
                    "ContentDetection config is stored in content_detection_configs table. " +
                    "Use IContentDetectionConfigRepository instead of ConfigService.");
            case ConfigType.Welcome:
                record.WelcomeConfig = json;
                break;
            case ConfigType.Log:
                record.LogConfig = json;
                break;
            case ConfigType.Moderation:
                record.ModerationConfig = json;
                break;
            case ConfigType.UrlFilter:
                record.BotProtectionConfig = json;
                break;
            case ConfigType.TelegramBot:
                record.TelegramBotConfig = json;
                break;
            case ConfigType.ServiceMessageDeletion:
                record.ServiceMessageDeletionConfig = json;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(configType), configType, "Unknown config type");
        }
    }

    private static string? GetConfigColumn(ConfigRecordDto record, ConfigType configType)
    {
        return configType switch
        {
            ConfigType.ContentDetection => throw new InvalidOperationException(
                "ContentDetection config is stored in content_detection_configs table. " +
                "Use IContentDetectionConfigRepository instead of ConfigService."),
            ConfigType.Welcome => record.WelcomeConfig,
            ConfigType.Log => record.LogConfig,
            ConfigType.Moderation => record.ModerationConfig,
            ConfigType.UrlFilter => record.BotProtectionConfig,
            ConfigType.TelegramBot => record.TelegramBotConfig,
            ConfigType.ServiceMessageDeletion => record.ServiceMessageDeletionConfig,
            _ => throw new ArgumentOutOfRangeException(nameof(configType), configType, "Unknown config type")
        };
    }

    // Cache PropertyInfo array for IsRecordEmpty - reflection cost paid once at startup
    // Only string properties are config columns; Id and ChatId are long so already excluded
    private static readonly System.Reflection.PropertyInfo[] ConfigStringProperties =
        typeof(ConfigRecordDto)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .ToArray();

    /// <summary>
    /// Checks if all config columns are empty (null or empty string).
    /// Uses reflection to automatically include all string properties,
    /// so new config columns don't require manual updates here.
    /// </summary>
    private static bool IsRecordEmpty(ConfigRecordDto record)
    {
        return ConfigStringProperties.All(p => string.IsNullOrEmpty((string?)p.GetValue(record)));
    }

    /// <summary>
    /// Merge two configuration objects by serializing to JSON and merging at the property level
    /// Chat-specific values override global values
    /// </summary>
    private static T MergeConfigs<T>(T globalConfig, T chatConfig) where T : class
    {
        // Serialize both configs to JSON objects
        var globalJson = JsonSerializer.SerializeToDocument(globalConfig, JsonOptions);
        var chatJson = JsonSerializer.SerializeToDocument(chatConfig, JsonOptions);

        // Create a merged object by copying global, then overlaying chat-specific
        var merged = new Dictionary<string, JsonElement>();

        // Add all global properties
        foreach (var property in globalJson.RootElement.EnumerateObject())
        {
            merged[property.Name] = property.Value;
        }

        // Override with chat-specific properties (only if not null/undefined)
        foreach (var property in chatJson.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Null)
            {
                merged[property.Name] = property.Value;
            }
        }

        // Serialize merged dictionary back to T
        var mergedJson = JsonSerializer.Serialize(merged, JsonOptions);
        return JsonSerializer.Deserialize<T>(mergedJson, JsonOptions)!;
    }
}
