using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Pure state storage for chat health data.
/// Singleton service that maintains an in-memory cache of chat health statuses.
/// No Telegram API calls - just stores and retrieves state.
/// </summary>
public class ChatHealthCache(ILogger<ChatHealthCache> logger) : IChatHealthCache
{
    private readonly ConcurrentDictionary<long, ChatHealthStatus> _healthCache = new();

    public event Action<ChatHealthStatus>? OnHealthUpdate;

    public ChatHealthStatus? GetCachedHealth(long chatId)
        => _healthCache.TryGetValue(chatId, out var health) ? health : null;

    public IReadOnlyList<ChatIdentity> GetHealthyChatIdentities()
    {
        var healthyChats = _healthCache
            .Where(kvp => kvp.Value.Status == ChatHealthStatusType.Healthy)
            .Select(kvp => kvp.Value.Chat)
            .ToList();

        if (_healthCache.Count == 0)
        {
            logger.LogWarning(
                "Health cache is empty - health check may not have run yet. " +
                "No chats will be excluded from moderation actions.");
        }
        else if (healthyChats.Count == 0)
        {
            logger.LogWarning(
                "No healthy chats found in cache ({TotalChats} total). " +
                "All moderation actions will be skipped until health check completes successfully.",
                _healthCache.Count);
        }
        else
        {
            logger.LogDebug(
                "Health gate: {HealthyCount}/{TotalCount} chats are healthy and actionable",
                healthyChats.Count,
                _healthCache.Count);
        }

        return healthyChats;
    }

    public void SetHealth(long chatId, ChatHealthStatus status)
    {
        _healthCache[chatId] = status;
        OnHealthUpdate?.Invoke(status);
    }

    public void RemoveHealth(long chatId)
    {
        _healthCache.TryRemove(chatId, out _);
    }

    public IReadOnlyDictionary<long, ChatHealthStatus> GetAllCachedHealth()
        => _healthCache;
}
