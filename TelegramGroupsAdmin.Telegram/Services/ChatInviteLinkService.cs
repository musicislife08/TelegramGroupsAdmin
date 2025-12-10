using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Implementation of chat invite link retrieval with database caching
/// Used by: /tempban, welcome system
/// </summary>
public class ChatInviteLinkService : IChatInviteLinkService
{
    private readonly ILogger<ChatInviteLinkService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TelegramBotClientFactory _botClientFactory;

    public ChatInviteLinkService(
        ILogger<ChatInviteLinkService> logger,
        IServiceProvider serviceProvider,
        TelegramBotClientFactory botClientFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _botClientFactory = botClientFactory;
    }

    public async Task<string?> GetInviteLinkAsync(
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
            return await FetchAndCacheInviteLinkAsync(chatId, configRepo, ct);
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

            return await FetchAndCacheInviteLinkAsync(chatId, configRepo, ct);
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
        long chatId,
        IConfigRepository configRepo,
        CancellationToken ct)
    {
        var operations = await _botClientFactory.GetOperationsAsync();

        // Get chat info from Telegram
        var chat = await operations.GetChatAsync(chatId, ct);

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
            currentLink = await operations.ExportChatInviteLinkAsync(chatId, ct);
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
