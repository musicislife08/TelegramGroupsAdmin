using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// Service for managing unified configuration storage with automatic global/chat merging
/// PERF-CFG-1: Uses IMemoryCache for 95% query reduction (200+ queries/hr → 10-15)
/// Routes ConfigType.ContentDetection to IContentDetectionConfigRepository (separate table)
/// </summary>
public class ConfigService(
    IConfigRepository configRepository,
    IContentDetectionConfigRepository contentDetectionConfigRepository,
    IMemoryCache cache,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ConfigService> logger) : IConfigService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15); // Sliding expiration

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
            cache.Remove($"cfg_{configType}_{chatId}");
            if (chatId != 0)
                cache.Remove($"cfg_effective_{configType}_{chatId}");
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
        cache.Remove(cacheKey);

        // Also invalidate effective config cache if chat-specific
        if (chatId != 0)
        {
            var effectiveCacheKey = $"cfg_effective_{configType}_{chatId}";
            cache.Remove(effectiveCacheKey);
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

        // Fast path: cache hit (99% of calls after warm-up)
        if (cache.TryGetValue<T>(cacheKey, out var cachedValue))
        {
            return cachedValue;
        }

        // Route ContentDetection to separate repository
        if (configType == ConfigType.ContentDetection)
        {
            ContentDetectionConfig? cdConfig;
            if (chatId == 0)
                cdConfig = await contentDetectionConfigRepository.GetGlobalConfigAsync();
            else
                cdConfig = await contentDetectionConfigRepository.GetByChatIdAsync(chatId);

            if (cdConfig != null)
            {
                cache.Set(cacheKey, cdConfig, new MemoryCacheEntryOptions { SlidingExpiration = CacheDuration });
            }
            return cdConfig as T;
        }

        // Slow path: cache miss - fetch from DB and populate cache
        var record = await configRepository.GetAsync(chatId);
        if (record == null)
        {
            return null;
        }

        var json = GetConfigColumn(record, configType);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var config = JsonSerializer.Deserialize<T>(json, JsonOptions);

        // Cache for 15 minutes (sliding expiration - extends on each access)
        cache.Set(cacheKey, config, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheDuration
        });

        return config;
    }

    public async ValueTask<T?> GetEffectiveAsync<T>(ConfigType configType, long chatId) where T : class
    {
        // Route ContentDetection to separate repository (has its own merge logic)
        if (configType == ConfigType.ContentDetection)
        {
            var cdCacheKey = $"cfg_effective_{configType}_{chatId}";
            if (cache.TryGetValue<T>(cdCacheKey, out var cachedCd))
                return cachedCd;

            // Repository handles global/chat merge with UseGlobal flags per sub-config
            var cdConfig = await contentDetectionConfigRepository.GetEffectiveConfigAsync(chatId);
            cache.Set(cdCacheKey, cdConfig, new MemoryCacheEntryOptions { SlidingExpiration = CacheDuration });
            return cdConfig as T;
        }

        // If requesting global config, just return it directly (uses cached GetAsync)
        if (chatId == 0)
        {
            return await GetAsync<T>(configType, 0);
        }

        // Cache the effective (merged) config too
        var effectiveCacheKey = $"cfg_effective_{configType}_{chatId}";

        if (cache.TryGetValue<T>(effectiveCacheKey, out var cachedEffective))
        {
            return cachedEffective;
        }

        // Get both global and chat-specific configs (both use cached GetAsync)
        var globalConfig = await GetAsync<T>(configType, 0);
        var chatConfig = await GetAsync<T>(configType, chatId);

        T? effectiveConfig;

        // If no chat-specific config, return global
        if (chatConfig == null)
        {
            effectiveConfig = globalConfig;
        }
        // If no global config, return chat-specific
        else if (globalConfig == null)
        {
            effectiveConfig = chatConfig;
        }
        // Merge: chat-specific overrides global
        else
        {
            effectiveConfig = MergeConfigs(globalConfig, chatConfig);
        }

        // Cache the effective config (sliding expiration)
        if (effectiveConfig != null)
        {
            cache.Set(effectiveCacheKey, effectiveConfig, new MemoryCacheEntryOptions
            {
                SlidingExpiration = CacheDuration
            });
        }

        return effectiveConfig;
    }

    public async Task DeleteAsync(ConfigType configType, long chatId)
    {
        // Route ContentDetection to separate repository
        if (configType == ConfigType.ContentDetection)
        {
            if (chatId != 0)
                await contentDetectionConfigRepository.DeleteChatConfigAsync(chatId);
            // Note: Can't delete global config (chat_id=0), it's the fallback

            cache.Remove($"cfg_{configType}_{chatId}");
            cache.Remove($"cfg_effective_{configType}_{chatId}");
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
        cache.Remove(cacheKey);

        // Also invalidate effective config cache if chat-specific
        if (chatId != 0)
        {
            var effectiveCacheKey = $"cfg_effective_{configType}_{chatId}";
            cache.Remove(effectiveCacheKey);
        }
    }

    /// <summary>
    /// Get the encrypted Telegram bot token from database (global config only, chat_id = 0)
    /// Returns decrypted token or null if not configured
    /// </summary>
    public async ValueTask<string?> GetTelegramBotTokenAsync()
    {
        const string cacheKey = "cfg_telegram_bot_token";

        // Check cache first
        if (cache.TryGetValue<string>(cacheKey, out var cachedToken))
        {
            return cachedToken;
        }

        // Load from database (global config, chat_id = 0)
        var record = await configRepository.GetAsync(0);
        if (record?.TelegramBotTokenEncrypted == null)
        {
            return null;
        }

        try
        {
            // Decrypt using Data Protection
            var protector = dataProtectionProvider.CreateProtector(DataProtectionPurposes.TelegramBotToken);
            var decryptedToken = protector.Unprotect(record.TelegramBotTokenEncrypted);

            // Cache for 15 minutes
            cache.Set(cacheKey, decryptedToken, new MemoryCacheEntryOptions
            {
                SlidingExpiration = CacheDuration
            });

            return decryptedToken;
        }
        catch (Exception)
        {
            // Decryption failed (corrupted data or wrong key)
            return null;
        }
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
        cache.Remove(cacheKey);
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
