using System.Collections.Concurrent;
using Telegram.Bot;

namespace TelegramGroupsAdmin.Telegram.Abstractions.Services;

/// <summary>
/// Factory for creating and caching Telegram Bot API clients
/// Supports both standard (api.telegram.org) and self-hosted Bot API servers
/// </summary>
public class TelegramBotClientFactory
{
    private readonly ConcurrentDictionary<string, ITelegramBotClient> _clients = new();

    /// <summary>
    /// Get or create a Telegram bot client with optional custom API server URL
    /// </summary>
    /// <param name="botToken">Bot token from BotFather</param>
    /// <param name="apiServerUrl">Optional custom Bot API server URL (e.g., http://bot-api-server:8081).
    /// When null/empty, uses standard api.telegram.org with 20MB file download limit.
    /// When set, uses self-hosted server with unlimited downloads.</param>
    /// <returns>Cached or newly created ITelegramBotClient instance</returns>
    public ITelegramBotClient GetOrCreate(string botToken, string? apiServerUrl = null)
    {
        // Create unique cache key that includes both token and API server URL
        // This allows caching separate clients for standard vs self-hosted mode
        var cacheKey = string.IsNullOrWhiteSpace(apiServerUrl)
            ? botToken
            : $"{botToken}::{apiServerUrl}";

        // Fast path: TryGetValue is lock-free and allocation-free (99.9% cache hit rate)
        if (_clients.TryGetValue(cacheKey, out var existingClient))
            return existingClient;

        // Slow path: Only called once per unique token+url combination (first call only)
        return _clients.GetOrAdd(cacheKey, _ =>
        {
            // Standard mode: Use default Bot API endpoint (api.telegram.org)
            if (string.IsNullOrWhiteSpace(apiServerUrl))
            {
                return new TelegramBotClient(botToken);
            }

            // Self-hosted mode: Create HttpClient with custom base address
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(apiServerUrl),
                Timeout = TimeSpan.FromMinutes(5) // Generous timeout for large file uploads/downloads
            };

            return new TelegramBotClient(botToken, httpClient);
        });
    }
}
