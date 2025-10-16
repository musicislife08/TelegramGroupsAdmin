using System.Text.Json;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// Service for managing unified configuration storage with automatic global/chat merging
/// </summary>
public class ConfigService(IConfigRepository configRepository) : IConfigService
{
    private readonly IConfigRepository _configRepository = configRepository;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task SaveAsync<T>(string configType, long? chatId, T config) where T : class
    {
        ArgumentNullException.ThrowIfNull(config);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var record = await _configRepository.GetAsync(chatId);

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

        await _configRepository.UpsertAsync(record);
    }

    public async Task<T?> GetAsync<T>(string configType, long? chatId) where T : class
    {
        var record = await _configRepository.GetAsync(chatId);
        if (record == null)
        {
            return null;
        }

        var json = GetConfigColumn(record, configType);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async Task<T?> GetEffectiveAsync<T>(string configType, long? chatId) where T : class
    {
        // If requesting global config, just return it directly
        if (chatId == null)
        {
            return await GetAsync<T>(configType, null);
        }

        // Get both global and chat-specific configs
        var globalConfig = await GetAsync<T>(configType, null);
        var chatConfig = await GetAsync<T>(configType, chatId);

        // If no chat-specific config, return global
        if (chatConfig == null)
        {
            return globalConfig;
        }

        // If no global config, return chat-specific
        if (globalConfig == null)
        {
            return chatConfig;
        }

        // Merge: chat-specific overrides global
        return MergeConfigs(globalConfig, chatConfig);
    }

    public async Task DeleteAsync(string configType, long? chatId)
    {
        var record = await _configRepository.GetAsync(chatId);
        if (record == null)
        {
            return;
        }

        // Clear the specific config column
        SetConfigColumn(record, configType, null);

        // If all config columns are null, delete the entire row
        if (IsRecordEmpty(record))
        {
            await _configRepository.DeleteAsync(chatId);
        }
        else
        {
            await _configRepository.UpsertAsync(record);
        }
    }

    private static void SetConfigColumn(ConfigRecordDto record, string configType, string? json)
    {
        switch (configType.ToLowerInvariant())
        {
            case "spam_detection":
                record.SpamDetectionConfig = json;
                break;
            case "welcome":
                record.WelcomeConfig = json;
                break;
            case "log":
                record.LogConfig = json;
                break;
            case "moderation":
                record.ModerationConfig = json;
                break;
            case "bot_protection":
                record.BotProtectionConfig = json;
                break;
            default:
                throw new ArgumentException($"Unknown config type: {configType}", nameof(configType));
        }
    }

    private static string? GetConfigColumn(ConfigRecordDto record, string configType)
    {
        return configType.ToLowerInvariant() switch
        {
            "spam_detection" => record.SpamDetectionConfig,
            "welcome" => record.WelcomeConfig,
            "log" => record.LogConfig,
            "moderation" => record.ModerationConfig,
            "bot_protection" => record.BotProtectionConfig,
            _ => throw new ArgumentException($"Unknown config type: {configType}", nameof(configType))
        };
    }

    private static bool IsRecordEmpty(ConfigRecordDto record)
    {
        return string.IsNullOrEmpty(record.SpamDetectionConfig)
            && string.IsNullOrEmpty(record.WelcomeConfig)
            && string.IsNullOrEmpty(record.LogConfig)
            && string.IsNullOrEmpty(record.ModerationConfig)
            && string.IsNullOrEmpty(record.BotProtectionConfig);
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
