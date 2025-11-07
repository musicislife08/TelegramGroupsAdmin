using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// One-time migration service to populate Telegram bot token from environment variables
/// Called during application startup after migrations run, before bot services start
/// Migrates TELEGRAM__BOTTOKEN from env vars to database (encrypted storage)
/// NOTE: ChatId is NOT migrated - the bot is multi-group and discovers chats dynamically
/// </summary>
public class TelegramConfigMigrationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramConfigMigrationService> _logger;

    public TelegramConfigMigrationService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TelegramConfigMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
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
        // Create scope to resolve scoped ConfigService
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

        // Check if Telegram bot token already exists in database
        var existingToken = await configService.GetTelegramBotTokenAsync();

        // If token exists, skip migration
        if (existingToken != null)
        {
            _logger.LogInformation("Telegram bot token already exists in database, skipping env var migration");
            return;
        }

        // Read bot token from environment variables
        var botToken = _configuration["Telegram:BotToken"];

        // If no env var found, skip migration
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogInformation("No Telegram bot token found in environment variables, skipping migration");
            return;
        }

        _logger.LogInformation("Migrating Telegram bot token from environment variables to database...");

        // Save bot token to encrypted database storage
        await configService.SaveTelegramBotTokenAsync(botToken);
        _logger.LogInformation("Migrated Telegram bot token to encrypted database storage");

        _logger.LogWarning(
            "Telegram bot token migrated from environment variables to database. " +
            "IMPORTANT: Remove TELEGRAM__BOTTOKEN from your environment configuration. " +
            "Future changes should be made via Settings → Telegram Bot UI.");
    }
}
