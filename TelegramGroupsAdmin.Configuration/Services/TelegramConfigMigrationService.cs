using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// One-time migration service to populate Telegram bot configuration from environment variables
/// Called during application startup after migrations run, before bot services start
/// Migrates TELEGRAM__BOTTOKEN, TELEGRAM__CHATID, TELEGRAM__APISERVERURL from env vars to database
/// </summary>
public class TelegramConfigMigrationService : IHostedService
{
    private readonly ConfigService _configService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramConfigMigrationService> _logger;

    public TelegramConfigMigrationService(
        ConfigService configService,
        IConfiguration configuration,
        ILogger<TelegramConfigMigrationService> logger)
    {
        _configService = configService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await MigrateTelegramConfigFromEnvironmentAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate Telegram configuration from environment variables");
            // Don't throw - allow application to start even if migration fails
            // User can configure manually via Settings UI
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Migrate Telegram configuration from environment variables to database
    /// This is a one-time operation that runs on first startup after migration
    /// </summary>
    private async Task MigrateTelegramConfigFromEnvironmentAsync(CancellationToken cancellationToken)
    {
        // Check if Telegram bot config already exists in database (chat_id = 0)
        var existingConfig = await _configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, 0);
        var existingToken = await _configService.GetTelegramBotTokenAsync();

        // If both token and config exist, skip migration
        if (existingToken != null && existingConfig != null)
        {
            _logger.LogInformation("Telegram bot configuration already exists in database, skipping env var migration");
            return;
        }

        // Read configuration from environment variables
        var botToken = _configuration["Telegram:BotToken"];
        var chatIdStr = _configuration["Telegram:ChatId"];
        var apiServerUrl = _configuration["Telegram:ApiServerUrl"];

        // If no env vars found, skip migration
        if (string.IsNullOrWhiteSpace(botToken) && string.IsNullOrWhiteSpace(chatIdStr))
        {
            _logger.LogInformation("No Telegram configuration found in environment variables, skipping migration");
            return;
        }

        _logger.LogInformation("Migrating Telegram bot configuration from environment variables to database...");

        // Parse ChatId (must be negative for groups)
        long? chatId = null;
        if (!string.IsNullOrWhiteSpace(chatIdStr) && long.TryParse(chatIdStr, out var parsedChatId))
        {
            chatId = parsedChatId;
        }

        // Save bot token (encrypted) if provided
        if (!string.IsNullOrWhiteSpace(botToken))
        {
            await _configService.SaveTelegramBotTokenAsync(botToken);
            _logger.LogInformation("Migrated Telegram bot token to encrypted database storage");
        }

        // Save config (JSONB) if ChatId or ApiServerUrl provided
        if (chatId.HasValue || !string.IsNullOrWhiteSpace(apiServerUrl))
        {
            var config = new TelegramBotConfig
            {
                BotEnabled = false, // Users must explicitly enable after verifying config
                ChatId = chatId,
                ApiServerUrl = string.IsNullOrWhiteSpace(apiServerUrl) ? null : apiServerUrl
            };

            await _configService.SaveAsync(ConfigType.TelegramBot, 0, config);
            _logger.LogInformation(
                "Migrated Telegram bot configuration to database: ChatId={ChatId}, ApiServerUrl={ApiServerUrl}",
                chatId,
                apiServerUrl ?? "(standard api.telegram.org)");
        }

        _logger.LogWarning(
            "Telegram bot configuration migrated from environment variables to database (chat_id=0). " +
            "IMPORTANT: Remove TELEGRAM__BOTTOKEN, TELEGRAM__CHATID, and TELEGRAM__APISERVERURL from your environment " +
            "configuration. Future changes should be made via Settings → Telegram Bot UI.");
    }
}
