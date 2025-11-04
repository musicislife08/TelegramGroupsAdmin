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
    /// <returns>Tuple of (BotToken, ApiServerUrl)</returns>
    /// <exception cref="InvalidOperationException">Thrown if bot token not configured</exception>
    /// <remarks>
    /// IMPORTANT: ChatId is NOT part of global config - the bot is multi-group and discovers
    /// chats dynamically when added to groups. Never add ChatId to this method's return value
    /// or require it in configuration validation.
    /// </remarks>
    public async Task<(string BotToken, string? ApiServerUrl)> LoadConfigAsync()
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

        // Load optional API server URL (JSONB column, stored at chat_id=0 for global config)
        var config = await configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, 0);
        var apiServerUrl = config?.ApiServerUrl;

        _logger.LogDebug(
            "Loaded Telegram bot configuration: ApiServerUrl={ApiServerUrl}",
            apiServerUrl ?? "(standard api.telegram.org)");

        return (botToken, apiServerUrl);
    }
}
