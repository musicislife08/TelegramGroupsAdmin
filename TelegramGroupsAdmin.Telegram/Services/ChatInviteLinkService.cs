using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for retrieving chat invite links from Telegram API
/// Caches links in database (configs.invite_link) to avoid repeated API calls
/// </summary>
public interface IChatInviteLinkService
{
    /// <summary>
    /// Get invite link for chat (from cache or Telegram API)
    /// Returns null if bot lacks permissions or chat is private without link
    /// </summary>
    Task<string?> GetInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken ct = default);

    /// <summary>
    /// Refresh invite link from Telegram API and update cache
    /// Use this to validate cached link is still valid (e.g., in health checks)
    /// </summary>
    Task<string?> RefreshInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation of chat invite link retrieval with database caching
/// Used by: /tempban, welcome system
/// </summary>
public class ChatInviteLinkService : IChatInviteLinkService
{
    private readonly ILogger<ChatInviteLinkService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ChatInviteLinkService(
        ILogger<ChatInviteLinkService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<string?> GetInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken ct = default)
    {
        try
        {
            // Step 1: Check database cache first (avoid Telegram API call if cached)
            using var scope = _serviceProvider.CreateScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<IConfigRepository>();

            var cachedConfig = await configRepo.GetByChatIdAsync(chatId, ct);
            if (cachedConfig?.InviteLink != null)
            {
                _logger.LogDebug("Using cached invite link for chat {ChatId}", chatId);
                return cachedConfig.InviteLink;
            }

            // Step 2: Not cached - fetch and cache
            return await FetchAndCacheInviteLinkAsync(botClient, chatId, configRepo, ct);
        }
        catch (Exception ex)
        {
            // Expected failure cases:
            // - Bot is not admin (lacks permission to export invite link)
            // - Transient API error
            // - Chat no longer exists
            // Health check should prevent most permission issues
            _logger.LogWarning(
                ex,
                "Failed to get invite link for chat {ChatId}. Bot may lack admin permissions or health check issue.",
                chatId);
            return null;
        }
    }

    public async Task<string?> RefreshInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken ct = default)
    {
        try
        {
            // Force refresh from Telegram API (ignore cache)
            using var scope = _serviceProvider.CreateScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<IConfigRepository>();

            _logger.LogDebug("Force refreshing invite link for chat {ChatId}", chatId);
            return await FetchAndCacheInviteLinkAsync(botClient, chatId, configRepo, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to refresh invite link for chat {ChatId}. Bot may lack admin permissions.",
                chatId);
            return null;
        }
    }

    /// <summary>
    /// Fetch invite link from Telegram API and cache in database
    /// </summary>
    private async Task<string?> FetchAndCacheInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        IConfigRepository configRepo,
        CancellationToken ct)
    {
        // Get chat info from Telegram
        var chat = await botClient.GetChat(chatId, ct);

        string? inviteLink;

        // Public group - use username link (e.g., https://t.me/groupname)
        if (!string.IsNullOrEmpty(chat.Username))
        {
            inviteLink = $"https://t.me/{chat.Username}";
            _logger.LogDebug("Got public invite link for chat {ChatId}: {Link}", chatId, inviteLink);
        }
        else
        {
            // Private group - use primary invite link (permanent and reusable)
            // ExportChatInviteLink returns the main link (same as the one in chat settings)
            inviteLink = await botClient.ExportChatInviteLink(chatId, ct);
            _logger.LogDebug("Exported primary invite link for private chat {ChatId}", chatId);
        }

        // Cache the invite link in database for future requests
        await configRepo.SaveInviteLinkAsync(chatId, inviteLink, ct);
        _logger.LogInformation("Cached invite link for chat {ChatId}", chatId);

        return inviteLink;
    }
}
