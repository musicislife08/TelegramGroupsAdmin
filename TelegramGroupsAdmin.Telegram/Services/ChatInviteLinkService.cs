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
            // Fetch current link from Telegram API
            // FetchAndCacheInviteLinkAsync will compare with cache and only update if different
            using var scope = _serviceProvider.CreateScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<IConfigRepository>();

            _logger.LogDebug("Refreshing invite link from Telegram for chat {ChatId}", chatId);

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
    /// Fetch current invite link from Telegram API and cache in database
    /// Only updates cache if link has changed (reduces unnecessary writes)
    /// </summary>
    private async Task<string?> FetchAndCacheInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        IConfigRepository configRepo,
        CancellationToken ct)
    {
        // Get chat info from Telegram
        var chat = await botClient.GetChat(chatId, ct);

        string? currentLink;

        // Public group - use username link (e.g., https://t.me/groupname)
        if (!string.IsNullOrEmpty(chat.Username))
        {
            currentLink = $"https://t.me/{chat.Username}";
            _logger.LogDebug("Got public invite link for chat {ChatId}: {Link}", chatId, currentLink);
        }
        else
        {
            // Private group - check if we already have a cached link
            // ExportChatInviteLink GENERATES a new link (revokes old), so we must avoid calling it
            var cachedConfig = await configRepo.GetByChatIdAsync(chatId, ct);

            if (cachedConfig?.InviteLink != null)
            {
                // Use cached link - don't call ExportChatInviteLink (it would revoke this one)
                _logger.LogDebug("Using existing cached invite link for private chat {ChatId}", chatId);
                return cachedConfig.InviteLink;
            }

            // No cached link - export the primary link (this WILL revoke any previous primary link)
            // This should only happen on first setup
            currentLink = await botClient.ExportChatInviteLink(chatId, ct);
            _logger.LogWarning(
                "Exported PRIMARY invite link for private chat {ChatId} - this revokes previous primary link! " +
                "Link: {Link}",
                chatId,
                currentLink);

            // Cache it so we never call ExportChatInviteLink again for this chat
            await configRepo.SaveInviteLinkAsync(chatId, currentLink, ct);
            return currentLink;
        }

        return currentLink;
    }
}
