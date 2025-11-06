using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;

namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Repository implementation for file scanning configuration
/// Stores config in configs.file_scanning_config JSONB column
/// </summary>
public class FileScanningConfigRepository : IFileScanningConfigRepository
{
    private readonly ILogger<FileScanningConfigRepository> _logger;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileScanningConfigRepository(
        ILogger<FileScanningConfigRepository> logger,
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

        // Merge: chat config overrides global
        // For now, we'll just return the chat config if it exists
        // TODO: Implement sophisticated merging logic if needed
        return chatConfig;
    }

    public async Task SaveAsync(FileScanningConfig config, long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        config.LastModified = DateTimeOffset.UtcNow;

        // Serialize to JSON
        var jsonConfig = JsonSerializer.Serialize(config, _jsonOptions);

        _logger.LogInformation("Saving file scanning config for {ChatType}", chatId == null ? "global" : $"chat {chatId}");

        // Find or create config record
        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken)
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
        var configRecord = await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken)
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
}
