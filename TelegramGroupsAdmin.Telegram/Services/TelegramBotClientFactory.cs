using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Factory for creating and caching Telegram Bot API clients.
/// Uses standard api.telegram.org endpoint with 20MB file download limit.
///
/// Uses a "refresh on change" pattern - tracks single active client, disposes old
/// client when token changes. This keeps resource usage low for homelab deployment.
///
/// Two usage patterns:
/// - GetBotClientAsync(): Returns raw client for polling (TelegramBotPollingHost only)
/// - GetOperationsAsync(): Returns ITelegramOperations wrapper (all other services)
/// </summary>
public class TelegramBotClientFactory : ITelegramBotClientFactory
{
    private readonly ITelegramConfigLoader _configLoader;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TelegramBotClientFactory> _logger;
    private readonly Lock _lock = new();

    // Single client/token pair (not dictionary) - replaced when token changes
    private string? _currentToken;
    private ITelegramBotClient? _currentClient;
    private TelegramOperations? _currentOperations;

    public TelegramBotClientFactory(
        ITelegramConfigLoader configLoader,
        ILoggerFactory loggerFactory)
    {
        _configLoader = configLoader;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TelegramBotClientFactory>();
    }

    /// <inheritdoc />
    public async Task<ITelegramBotClient> GetBotClientAsync()
    {
        var botToken = await _configLoader.LoadConfigAsync();
        return GetOrRefresh(botToken);
    }

    /// <inheritdoc />
    public async Task<ITelegramOperations> GetOperationsAsync()
    {
        var botToken = await _configLoader.LoadConfigAsync();
        GetOrRefresh(botToken); // Ensure client is current

        lock (_lock)
        {
            return _currentOperations
                ?? throw new InvalidOperationException("Operations not initialized - GetOrRefresh failed to create instance");
        }
    }

    private ITelegramBotClient GetOrRefresh(string botToken)
    {
        lock (_lock)
        {
            // Fast path: token unchanged, return cached client
            if (_currentToken == botToken && _currentClient != null)
                return _currentClient;

            // Token changed or first call - replace old client
            if (_currentClient != null)
            {
                _logger.LogInformation("Bot token changed, replacing client");
            }

            // Create new client and wrapper
            _currentToken = botToken;
            _currentClient = new TelegramBotClient(botToken);
            _currentOperations = new TelegramOperations(
                _currentClient,
                _loggerFactory.CreateLogger<TelegramOperations>());

            _logger.LogDebug("Created new Telegram client");
            return _currentClient;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _currentClient = null;
            _currentOperations = null;
            _currentToken = null;
        }
    }
}
