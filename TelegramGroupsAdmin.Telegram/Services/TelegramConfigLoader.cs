using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized service for loading Telegram bot configuration from database
/// Replaces IOptions&lt;TelegramOptions&gt; pattern with database-backed configuration
/// Used by all services that need bot credentials (token)
/// </summary>
public class TelegramConfigLoader : ITelegramConfigLoader
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
    /// Load Telegram bot token from database (global config, chat_id=0)
    /// </summary>
    /// <returns>Bot token string</returns>
    /// <exception cref="InvalidOperationException">Thrown if bot token not configured</exception>
    /// <remarks>
    /// IMPORTANT: ChatId is NOT part of global config - the bot is multi-group and discovers
    /// chats dynamically when added to groups. Never add ChatId to this method's return value
    /// or require it in configuration validation.
    /// </remarks>
    /// <inheritdoc />
    public async Task<string> LoadConfigAsync()
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

        _logger.LogDebug("Loaded Telegram bot configuration successfully");

        return botToken;
    }
}
