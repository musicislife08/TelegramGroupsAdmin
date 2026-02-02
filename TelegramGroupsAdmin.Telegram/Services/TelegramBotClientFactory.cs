using Microsoft.Extensions.Logging;
using Telegram.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Factory for creating and caching Telegram Bot API clients.
/// Uses standard api.telegram.org endpoint with 20MB file download limit.
///
/// Uses a "refresh on change" pattern - tracks single active client, disposes old
/// client when token changes. This keeps resource usage low for homelab deployment.
///
/// Only used by Bot Handlers layer - services and application code should use IBot*Service interfaces.
/// </summary>
public class TelegramBotClientFactory : ITelegramBotClientFactory
{
    private readonly ITelegramConfigLoader _configLoader;
    private readonly ILogger<TelegramBotClientFactory> _logger;
    private readonly Lock _lock = new();

    // Single client/token pair (not dictionary) - replaced when token changes
    private string? _currentToken;
    private ITelegramBotClient? _currentClient;
    private ITelegramApiClient? _currentApiClient;

    public TelegramBotClientFactory(
        ITelegramConfigLoader configLoader,
        ILoggerFactory loggerFactory)
    {
        _configLoader = configLoader;
        _logger = loggerFactory.CreateLogger<TelegramBotClientFactory>();
    }

    /// <inheritdoc />
    public async Task<ITelegramBotClient> GetBotClientAsync()
    {
        var botToken = await _configLoader.LoadConfigAsync();
        GetOrRefresh(botToken);
        return _currentClient!;
    }

    /// <inheritdoc />
    public async Task<ITelegramApiClient> GetApiClientAsync()
    {
        var botToken = await _configLoader.LoadConfigAsync();
        GetOrRefresh(botToken);
        return _currentApiClient!;
    }

    private void GetOrRefresh(string botToken)
    {
        lock (_lock)
        {
            // Fast path: token unchanged, return cached client
            if (_currentToken == botToken && _currentClient != null)
                return;

            // Token changed or first call - replace old client
            if (_currentClient != null)
            {
                _logger.LogInformation("Bot token changed, replacing client");
            }

            // Create new client and wrapper
            _currentToken = botToken;
            _currentClient = new TelegramBotClient(botToken);
            _currentApiClient = new TelegramApiClient(_currentClient);

            _logger.LogDebug("Created new Telegram client");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _currentClient = null;
            _currentApiClient = null;
            _currentToken = null;
        }
    }
}
