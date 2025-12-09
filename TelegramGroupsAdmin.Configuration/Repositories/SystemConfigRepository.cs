using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;

namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Repository implementation for system-wide configuration
/// Handles global configs (API keys, service settings) and per-chat config overrides
/// </summary>
public class SystemConfigRepository : ISystemConfigRepository
{
    private readonly ILogger<SystemConfigRepository> _logger;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly JsonSerializerOptions _jsonOptions;

    public SystemConfigRepository(
        ILogger<SystemConfigRepository> logger,
        IDbContextFactory<AppDbContext> contextFactory,
        IDataProtectionProvider dataProtectionProvider)
    {
        _logger = logger;
        _contextFactory = contextFactory;
        _dataProtectionProvider = dataProtectionProvider;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<FileScanningConfig> GetAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Load global config
        var globalConfig = await LoadConfigFromDbAsync(context, null, cancellationToken);

        // If no chat ID specified, return global
        if (chatId == null)
        {
            return globalConfig ?? new FileScanningConfig();
        }

        // Load chat-specific config
        var chatConfig = await LoadConfigFromDbAsync(context, chatId, cancellationToken);

        // If no chat config, return global
        if (chatConfig == null)
        {
            return globalConfig ?? new FileScanningConfig();
        }

        // Merge: chat config overrides global defaults (deep merge of nested objects)
        return MergeConfigs(globalConfig ?? new FileScanningConfig(), chatConfig);
    }

    public async Task SaveAsync(FileScanningConfig config, long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        config.LastModified = DateTimeOffset.UtcNow;

        // Serialize to JSON
        var jsonConfig = JsonSerializer.Serialize(config, _jsonOptions);

        _logger.LogInformation("Saving file scanning config for {ChatType}", chatId == null ? "global" : $"chat {chatId}");

        // Find or create config record (normalize null to 0 for global config)
        var normalizedChatId = chatId ?? 0;
        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == normalizedChatId, cancellationToken)
            ;

        if (configRecord == null)
        {
            // Create new record
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = chatId ?? 0,
                FileScanningConfig = jsonConfig,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await context.Configs.AddAsync(configRecord, cancellationToken);
        }
        else
        {
            // Update existing record
            configRecord.FileScanningConfig = jsonConfig;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("File scanning config saved successfully for {ChatType}", chatId == null ? "global" : $"chat {chatId}");
    }

    public async Task DeleteAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        _logger.LogInformation("Deleting file scanning config for chat {ChatId}", chatId);

        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken)
            ;

        if (configRecord != null)
        {
            configRecord.FileScanningConfig = null;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("File scanning config deleted for chat {ChatId}", chatId);
        }
    }

    /// <summary>
    /// Load configuration from database for a specific chat ID
    /// </summary>
    private async Task<FileScanningConfig?> LoadConfigFromDbAsync(
        AppDbContext context,
        long? chatId,
        CancellationToken cancellationToken)
    {
        // Normalize null to 0 for global config (SQL NULL comparison doesn't work with ==)
        var normalizedChatId = chatId ?? 0;
        var configRecord = await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == normalizedChatId, cancellationToken)
            ;

        if (configRecord?.FileScanningConfig == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FileScanningConfig>(configRecord.FileScanningConfig, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize file scanning config for {ChatType}",
                chatId == null ? "global" : $"chat {chatId}");
            return null;
        }
    }

    public async Task<ApiKeysConfig?> GetApiKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // API keys are global only (chat_id = 0)
        var configRecord = await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken)
            ;

        if (configRecord?.ApiKeys == null)
        {
            return null;
        }

        try
        {
            // Decrypt using Data Protection
            var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.ApiKeys);
            var decryptedJson = protector.Unprotect(configRecord.ApiKeys);

            // Deserialize from JSON
            return JsonSerializer.Deserialize<ApiKeysConfig>(decryptedJson, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt/deserialize API keys");
            return null;
        }
    }

    public async Task SaveApiKeysAsync(ApiKeysConfig apiKeys, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        _logger.LogInformation("Saving API keys to encrypted database storage");

        // Serialize to JSON
        var jsonKeys = JsonSerializer.Serialize(apiKeys, _jsonOptions);

        // Encrypt using Data Protection
        var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.ApiKeys);
        var encryptedKeys = protector.Protect(jsonKeys);

        // Find or create global config record (chat_id = 0)
        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken)
            ;

        if (configRecord == null)
        {
            // Create new global config record
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = 0,
                ApiKeys = encryptedKeys,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await context.Configs.AddAsync(configRecord, cancellationToken);
        }
        else
        {
            // Update existing global config
            configRecord.ApiKeys = encryptedKeys;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("API keys saved successfully to encrypted database storage");
    }

    public async Task<OpenAIConfig?> GetOpenAIConfigAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // OpenAI config is global only (chat_id = 0)
        var configRecord = await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord?.OpenAIConfig == null)
        {
            // Return default config if not set
            return new OpenAIConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<OpenAIConfig>(configRecord.OpenAIConfig, _jsonOptions) ?? new OpenAIConfig();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize OpenAI config");
            return new OpenAIConfig();
        }
    }

    public async Task SaveOpenAIConfigAsync(OpenAIConfig config, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        _logger.LogInformation("Saving OpenAI configuration to database");

        // Serialize to JSON
        var jsonConfig = JsonSerializer.Serialize(config, _jsonOptions);

        // Find or create global config record (chat_id = 0)
        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord == null)
        {
            // Create new global config record
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = 0,
                OpenAIConfig = jsonConfig,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await context.Configs.AddAsync(configRecord, cancellationToken);
        }
        else
        {
            // Update existing global config
            configRecord.OpenAIConfig = jsonConfig;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("OpenAI configuration saved successfully");
    }

    public async Task<SendGridConfig?> GetSendGridConfigAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // SendGrid config is global only (chat_id = 0)
        var configRecord = await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord?.SendGridConfig == null)
        {
            // Return default config if not set
            return new SendGridConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<SendGridConfig>(configRecord.SendGridConfig, _jsonOptions) ?? new SendGridConfig();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize SendGrid config");
            return new SendGridConfig();
        }
    }

    public async Task SaveSendGridConfigAsync(SendGridConfig config, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        _logger.LogInformation("Saving SendGrid configuration to database");

        // Serialize to JSON
        var jsonConfig = JsonSerializer.Serialize(config, _jsonOptions);

        // Find or create global config record (chat_id = 0)
        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord == null)
        {
            // Create new global config record
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = 0,
                SendGridConfig = jsonConfig,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await context.Configs.AddAsync(configRecord, cancellationToken);
        }
        else
        {
            // Update existing global config
            configRecord.SendGridConfig = jsonConfig;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("SendGrid configuration saved successfully");
    }

    public async Task<WebPushConfig> GetWebPushConfigAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Web Push config is global only (chat_id = 0)
        var configRecord = await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord?.WebPushConfig == null)
        {
            // Return default config if not set
            return new WebPushConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<WebPushConfig>(configRecord.WebPushConfig, _jsonOptions) ?? new WebPushConfig();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Web Push config");
            return new WebPushConfig();
        }
    }

    public async Task SaveWebPushConfigAsync(WebPushConfig config, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        _logger.LogInformation("Saving Web Push configuration to database");

        // Serialize to JSON
        var jsonConfig = JsonSerializer.Serialize(config, _jsonOptions);

        // Find or create global config record (chat_id = 0)
        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord == null)
        {
            // Create new global config record
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = 0,
                WebPushConfig = jsonConfig,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await context.Configs.AddAsync(configRecord, cancellationToken);
        }
        else
        {
            // Update existing global config
            configRecord.WebPushConfig = jsonConfig;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Web Push configuration saved successfully");
    }

    public async Task<string?> GetVapidPrivateKeyAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // VAPID key is global only (chat_id = 0)
        var configRecord = await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord?.VapidPrivateKeyEncrypted == null)
        {
            return null;
        }

        try
        {
            // Decrypt using Data Protection
            var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.VapidPrivateKey);
            return protector.Unprotect(configRecord.VapidPrivateKeyEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt VAPID private key");
            return null;
        }
    }

    public async Task SaveVapidPrivateKeyAsync(string privateKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        _logger.LogInformation("Saving VAPID private key to encrypted database storage");

        // Encrypt using Data Protection
        var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.VapidPrivateKey);
        var encryptedKey = protector.Protect(privateKey);

        // Find or create global config record (chat_id = 0)
        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord == null)
        {
            // Create new global config record
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = 0,
                VapidPrivateKeyEncrypted = encryptedKey,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await context.Configs.AddAsync(configRecord, cancellationToken);
        }
        else
        {
            // Update existing global config
            configRecord.VapidPrivateKeyEncrypted = encryptedKey;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("VAPID private key saved successfully to encrypted database storage");
    }

    public async Task<bool> HasVapidKeysAsync(CancellationToken cancellationToken = default)
    {
        var webPushConfig = await GetWebPushConfigAsync(cancellationToken);
        if (webPushConfig.HasVapidPublicKey())
        {
            var privateKey = await GetVapidPrivateKeyAsync(cancellationToken);
            return !string.IsNullOrWhiteSpace(privateKey);
        }

        return false;
    }

    public async Task<AIProviderConfig?> GetAIProviderConfigAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // AI provider config is global only (chat_id = 0)
        var configRecord = await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord?.AIProviderConfig == null)
        {
            // Return null if not configured (migration not run yet)
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AIProviderConfig>(configRecord.AIProviderConfig, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize AI provider config");
            return null;
        }
    }

    public async Task SaveAIProviderConfigAsync(AIProviderConfig config, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        _logger.LogInformation("Saving AI provider configuration to database");

        // Serialize to JSON
        var jsonConfig = JsonSerializer.Serialize(config, _jsonOptions);

        // Find or create global config record (chat_id = 0)
        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == 0, cancellationToken);

        if (configRecord == null)
        {
            // Create new global config record
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = 0,
                AIProviderConfig = jsonConfig,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await context.Configs.AddAsync(configRecord, cancellationToken);
        }
        else
        {
            // Update existing global config
            configRecord.AIProviderConfig = jsonConfig;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("AI provider configuration saved successfully");
    }

    /// <summary>
    /// Deep merge chat config onto global config
    /// Chat values override global defaults for all properties
    /// </summary>
    private static FileScanningConfig MergeConfigs(FileScanningConfig globalConfig, FileScanningConfig chatConfig)
    {
        return new FileScanningConfig
        {
            // Merge Tier1 config
            Tier1 = new Tier1Config
            {
                ClamAV = new ClamAVConfig
                {
                    Enabled = chatConfig.Tier1.ClamAV.Enabled != globalConfig.Tier1.ClamAV.Enabled
                        ? chatConfig.Tier1.ClamAV.Enabled
                        : globalConfig.Tier1.ClamAV.Enabled,
                    Host = chatConfig.Tier1.ClamAV.Host != new ClamAVConfig().Host
                        ? chatConfig.Tier1.ClamAV.Host
                        : globalConfig.Tier1.ClamAV.Host,
                    Port = chatConfig.Tier1.ClamAV.Port != new ClamAVConfig().Port
                        ? chatConfig.Tier1.ClamAV.Port
                        : globalConfig.Tier1.ClamAV.Port,
                    TimeoutSeconds = chatConfig.Tier1.ClamAV.TimeoutSeconds != new ClamAVConfig().TimeoutSeconds
                        ? chatConfig.Tier1.ClamAV.TimeoutSeconds
                        : globalConfig.Tier1.ClamAV.TimeoutSeconds
                }
            },

            // Merge Tier2 config
            Tier2 = new Tier2Config
            {
                CloudQueuePriority = chatConfig.Tier2.CloudQueuePriority.SequenceEqual(new Tier2Config().CloudQueuePriority)
                    ? globalConfig.Tier2.CloudQueuePriority
                    : chatConfig.Tier2.CloudQueuePriority,
                VirusTotal = new VirusTotalConfig
                {
                    Enabled = chatConfig.Tier2.VirusTotal.Enabled != globalConfig.Tier2.VirusTotal.Enabled
                        ? chatConfig.Tier2.VirusTotal.Enabled
                        : globalConfig.Tier2.VirusTotal.Enabled,
                    DailyLimit = chatConfig.Tier2.VirusTotal.DailyLimit != new VirusTotalConfig().DailyLimit
                        ? chatConfig.Tier2.VirusTotal.DailyLimit
                        : globalConfig.Tier2.VirusTotal.DailyLimit,
                    PerMinuteLimit = chatConfig.Tier2.VirusTotal.PerMinuteLimit != new VirusTotalConfig().PerMinuteLimit
                        ? chatConfig.Tier2.VirusTotal.PerMinuteLimit
                        : globalConfig.Tier2.VirusTotal.PerMinuteLimit
                },
                FailOpenWhenExhausted = chatConfig.Tier2.FailOpenWhenExhausted != new Tier2Config().FailOpenWhenExhausted
                    ? chatConfig.Tier2.FailOpenWhenExhausted
                    : globalConfig.Tier2.FailOpenWhenExhausted
            },

            // Merge General config
            General = new GeneralConfig
            {
                CacheEnabled = chatConfig.General.CacheEnabled != new GeneralConfig().CacheEnabled
                    ? chatConfig.General.CacheEnabled
                    : globalConfig.General.CacheEnabled,
                CacheTtlHours = chatConfig.General.CacheTtlHours != new GeneralConfig().CacheTtlHours
                    ? chatConfig.General.CacheTtlHours
                    : globalConfig.General.CacheTtlHours,
                ScanFileTypes = chatConfig.General.ScanFileTypes.SequenceEqual(new GeneralConfig().ScanFileTypes)
                    ? globalConfig.General.ScanFileTypes
                    : chatConfig.General.ScanFileTypes,
                MaxFileSizeBytes = chatConfig.General.MaxFileSizeBytes != new GeneralConfig().MaxFileSizeBytes
                    ? chatConfig.General.MaxFileSizeBytes
                    : globalConfig.General.MaxFileSizeBytes,
                AlwaysRunForAllUsers = chatConfig.General.AlwaysRunForAllUsers != new GeneralConfig().AlwaysRunForAllUsers
                    ? chatConfig.General.AlwaysRunForAllUsers
                    : globalConfig.General.AlwaysRunForAllUsers
            },

            // Use chat LastModified if present, otherwise global
            LastModified = chatConfig.LastModified > globalConfig.LastModified
                ? chatConfig.LastModified
                : globalConfig.LastModified
        };
    }
}
