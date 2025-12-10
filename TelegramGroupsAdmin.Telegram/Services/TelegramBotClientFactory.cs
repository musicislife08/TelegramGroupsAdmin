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
/// Three usage patterns:
/// - GetBotClientAsync(): Loads token from database (recommended for most services)
/// - GetOperationsAsync(): Returns ITelegramOperations wrapper (recommended for testable services)
/// - GetOrCreate(token): Direct token injection (used by TelegramAdminBotService which caches token)
/// </summary>
public class TelegramBotClientFactory : IDisposable
{
    private readonly TelegramConfigLoader _configLoader;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TelegramBotClientFactory> _logger;
    private readonly object _lock = new();

    // Single client/token pair (not dictionary) - replaced when token changes
    private string? _currentToken;
    private ITelegramBotClient? _currentClient;
    private TelegramOperations? _currentOperations;

    public TelegramBotClientFactory(
        TelegramConfigLoader configLoader,
        ILoggerFactory loggerFactory)
    {
        _configLoader = configLoader;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TelegramBotClientFactory>();
    }

    /// <summary>
    /// Get ITelegramBotClient using token loaded from database configuration.
    /// Replaces old client if token has changed.
    /// </summary>
    /// <returns>Current ITelegramBotClient instance</returns>
    public virtual async Task<ITelegramBotClient> GetBotClientAsync()
    {
        var botToken = await _configLoader.LoadConfigAsync();
        return GetOrRefresh(botToken);
    }

    /// <summary>
    /// Get ITelegramOperations using token loaded from database configuration.
    /// Returns mockable wrapper around the current ITelegramBotClient.
    /// </summary>
    /// <returns>Current ITelegramOperations instance</returns>
    /// <remarks>
    /// This is the recommended method for services that need Telegram API access.
    /// ITelegramOperations can be mocked with NSubstitute for unit testing.
    /// </remarks>
    public virtual async Task<ITelegramOperations> GetOperationsAsync()
    {
        var botToken = await _configLoader.LoadConfigAsync();
        GetOrRefresh(botToken); // Ensure client is current

        lock (_lock)
        {
            return _currentOperations!;
        }
    }

    /// <summary>
    /// Get or create client using provided token, replacing old client if token changed.
    /// Use this when you already have the token (e.g., TelegramAdminBotService caches it).
    /// </summary>
    /// <param name="botToken">Bot token from BotFather</param>
    /// <returns>Current ITelegramBotClient instance</returns>
    public virtual ITelegramBotClient GetOrCreate(string botToken)
    {
        return GetOrRefresh(botToken);
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
