using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized service for loading Telegram bot configuration from database
/// Replaces IOptions&lt;TelegramOptions&gt; pattern with database-backed configuration
/// Used by all services that need bot credentials (token, chat ID, API server URL)
/// </summary>
public class TelegramConfigLoader
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramConfigLoader> _logger;

    public TelegramConfigLoader(
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramConfigLoader> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Load Telegram bot configuration from database (global config, chat_id=0)
    /// </summary>
    /// <returns>Tuple of (BotToken, ChatId, ApiServerUrl)</returns>
    /// <exception cref="InvalidOperationException">Thrown if bot token or chat ID not configured</exception>
    public async Task<(string BotToken, long ChatId, string? ApiServerUrl)> LoadConfigAsync()
    {
        // Create scope to resolve scoped ConfigService
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

        // Load bot token (encrypted column)
        var botToken = await configService.GetTelegramBotTokenAsync();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogError("Telegram bot token not configured in database");
            throw new InvalidOperationException(
                "Telegram bot token not configured. Please configure via Settings → Telegram Bot or set TELEGRAM__BOTTOKEN environment variable and restart.");
        }

        // Load config (JSONB column)
        var config = await configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, 0);
        if (config?.ChatId == null)
        {
            _logger.LogError("Telegram chat ID not configured in database");
            throw new InvalidOperationException(
                "Telegram chat ID not configured. Please configure via Settings → Telegram Bot or set TELEGRAM__CHATID environment variable and restart.");
        }

        _logger.LogDebug(
            "Loaded Telegram bot configuration: ChatId={ChatId}, ApiServerUrl={ApiServerUrl}",
            config.ChatId.Value,
            config.ApiServerUrl ?? "(standard api.telegram.org)");

        return (botToken, config.ChatId.Value, config.ApiServerUrl);
    }
}
