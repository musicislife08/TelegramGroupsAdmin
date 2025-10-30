using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// One-time migration service to populate api_keys column from environment variables
/// Called during application startup after migrations run
/// </summary>
public class ApiKeyMigrationService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IConfiguration _configuration;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<ApiKeyMigrationService> _logger;

    public ApiKeyMigrationService(
        IDbContextFactory<AppDbContext> contextFactory,
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ApiKeyMigrationService> logger)
    {
        _contextFactory = contextFactory;
        _configuration = configuration;
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    /// <summary>
    /// Migrate API keys from environment variables to encrypted database column
    /// This is a one-time operation that runs on first startup after migration
    /// </summary>
    public async Task MigrateApiKeysFromEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get or create global config record (chat_id = NULL)
        var globalConfig = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == null, cancellationToken);

        // If api_keys already populated, skip migration
        if (globalConfig?.ApiKeys != null)
        {
            _logger.LogInformation("API keys already migrated to database, skipping env var migration");
            return;
        }

        // Read API keys from environment variables
        var apiKeys = new ApiKeysConfig
        {
            VirusTotal = _configuration["VirusTotal:ApiKey"]
        };

        // If no API keys found in env vars, skip migration
        if (!apiKeys.HasAnyKey())
        {
            _logger.LogInformation("No API keys found in environment variables, skipping migration");
            return;
        }

        _logger.LogInformation("Migrating API keys from environment variables to encrypted database storage...");

        // Serialize to JSON
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        var apiKeysJson = JsonSerializer.Serialize(apiKeys, jsonOptions);

        // Encrypt using Data Protection
        var protector = _dataProtectionProvider.CreateProtector("ApiKeys");
        var encryptedApiKeys = protector.Protect(apiKeysJson);

        // Save to database
        if (globalConfig == null)
        {
            // Create new global config record
            globalConfig = new Data.Models.ConfigRecordDto
            {
                ChatId = null,
                ApiKeys = encryptedApiKeys,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await context.Configs.AddAsync(globalConfig, cancellationToken);
        }
        else
        {
            // Update existing global config
            globalConfig.ApiKeys = encryptedApiKeys;
            globalConfig.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("✅ API keys successfully migrated to database and encrypted");
        _logger.LogWarning("⚠️  You can now remove API key environment variables (VirusTotal:ApiKey, etc.) - they are stored encrypted in the database");
    }
}
