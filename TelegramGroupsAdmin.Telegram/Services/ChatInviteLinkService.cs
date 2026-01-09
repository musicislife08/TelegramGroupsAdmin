using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Telegram.Extensions;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Implementation of chat invite link retrieval with database caching
/// Used by: /tempban, welcome system
/// </summary>
public class ChatInviteLinkService : IChatInviteLinkService
{
    private readonly ILogger<ChatInviteLinkService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelegramBotClientFactory _botClientFactory;

    public ChatInviteLinkService(
        ILogger<ChatInviteLinkService> logger,
        IServiceProvider serviceProvider,
        ITelegramBotClientFactory botClientFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _botClientFactory = botClientFactory;
    }

    public async Task<string?> GetInviteLinkAsync(
        Chat chat,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: Check database cache first (avoid Telegram API call if cached)
            using var scope = _serviceProvider.CreateScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<IConfigRepository>();

            var cachedConfig = await configRepo.GetByChatIdAsync(chat.Id, cancellationToken);
            if (cachedConfig?.InviteLink != null)
            {
                _logger.LogDebug("Using cached invite link for {Chat}", chat.ToLogDebug());
                return cachedConfig.InviteLink;
            }

            // Step 2: Not cached - fetch and cache
            return await FetchAndCacheInviteLinkAsync(chat, configRepo, cancellationToken);
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
                "Failed to get invite link for {Chat}. Bot may lack admin permissions or health check issue.",
                chat.ToLogDebug());
            return null;
        }
    }

    public async Task<string?> RefreshInviteLinkAsync(
        Chat chat,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Fetch current link from Telegram API
            // FetchAndCacheInviteLinkAsync will compare with cache and only update if different
            using var scope = _serviceProvider.CreateScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<IConfigRepository>();

            _logger.LogDebug("Refreshing invite link from Telegram for {Chat}", chat.ToLogDebug());

            return await FetchAndCacheInviteLinkAsync(chat, configRepo, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to refresh invite link for {Chat}. Bot may lack admin permissions.",
                chat.ToLogDebug());
            return null;
        }
    }

    /// <summary>
    /// Fetch current invite link from Telegram API and cache in database
    /// Only updates cache if link has changed (reduces unnecessary writes)
    /// </summary>
    private async Task<string?> FetchAndCacheInviteLinkAsync(
        Chat chat,
        IConfigRepository configRepo,
        CancellationToken cancellationToken)
    {
        var operations = await _botClientFactory.GetOperationsAsync();

        string? currentLink;

        // Public group - use username link (e.g., https://t.me/groupname)
        if (!string.IsNullOrEmpty(chat.Username))
        {
            currentLink = $"https://t.me/{chat.Username}";
            _logger.LogDebug("Got public invite link for {Chat}: {Link}", chat.ToLogDebug(), currentLink);

            // Cache public group link too (username could change)
            var cachedConfig = await configRepo.GetByChatIdAsync(chat.Id, cancellationToken);
            if (cachedConfig?.InviteLink != currentLink)
            {
                await configRepo.SaveInviteLinkAsync(chat.Id, currentLink, cancellationToken);
                _logger.LogDebug("Cached public invite link for {Chat}", chat.ToLogDebug());
            }

            return currentLink;
        }
        else
        {
            // Private group - check if we already have a cached link
            // ExportChatInviteLink GENERATES a new link (revokes old), so we must avoid calling it
            var cachedConfig = await configRepo.GetByChatIdAsync(chat.Id, cancellationToken);

            if (cachedConfig?.InviteLink != null)
            {
                // Use cached link - don't call ExportChatInviteLink (it would revoke this one)
                _logger.LogDebug("Using existing cached invite link for private {Chat}", chat.ToLogDebug());
                return cachedConfig.InviteLink;
            }

            // No cached link - export the primary link (this WILL revoke any previous primary link)
            // This should only happen on first setup
            currentLink = await operations.ExportChatInviteLinkAsync(chat.Id, cancellationToken);
            _logger.LogWarning(
                "Exported PRIMARY invite link for private {Chat} - this revokes previous primary link! " +
                "Link: {Link}",
                chat.ToLogDebug(),
                currentLink);

            // Cache it so we never call ExportChatInviteLink again for this chat
            await configRepo.SaveInviteLinkAsync(chat.Id, currentLink, cancellationToken);
            return currentLink;
        }
    }
}
