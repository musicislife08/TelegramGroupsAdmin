using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Repository implementation for file scanning configuration
/// Stores config in configs.file_scanning_config JSONB column
/// </summary>
public class FileScanningConfigRepository : IFileScanningConfigRepository
{
    private readonly ILogger<FileScanningConfigRepository> _logger;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileScanningConfigRepository(
        ILogger<FileScanningConfigRepository> logger,
        IDbContextFactory<AppDbContext> contextFactory)
    {
        _logger = logger;
        _contextFactory = contextFactory;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<FileScanningConfig> GetAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Load global config
        var globalConfig = await LoadConfigFromDbAsync(context, null, cancellationToken).ConfigureAwait(false);

        // If no chat ID specified, return global
        if (chatId == null)
        {
            return globalConfig ?? new FileScanningConfig();
        }

        // Load chat-specific config
        var chatConfig = await LoadConfigFromDbAsync(context, chatId, cancellationToken).ConfigureAwait(false);

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
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        config.LastModified = DateTimeOffset.UtcNow;

        // Serialize to JSON
        var jsonConfig = JsonSerializer.Serialize(config, _jsonOptions);

        _logger.LogInformation("Saving file scanning config for {ChatType}", chatId == null ? "global" : $"chat {chatId}");

        // Find or create config record
        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken)
            .ConfigureAwait(false);

        if (configRecord == null)
        {
            // Create new record
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = chatId,
                FileScanningConfig = jsonConfig,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await context.Configs.AddAsync(configRecord, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Update existing record
            configRecord.FileScanningConfig = jsonConfig;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("File scanning config saved successfully for {ChatType}", chatId == null ? "global" : $"chat {chatId}");
    }

    public async Task DeleteAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Deleting file scanning config for chat {ChatId}", chatId);

        var configRecord = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken)
            .ConfigureAwait(false);

        if (configRecord != null)
        {
            configRecord.FileScanningConfig = null;
            configRecord.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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
            .ConfigureAwait(false);

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
}
