using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Service layer for Telegram chat operations.
/// Wraps IBotChatHandler with caching and database integration.
/// </summary>
public class BotChatService : IBotChatService
{
    private readonly IBotChatHandler _chatHandler;
    private readonly IBotChatHealthService _chatHealthService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BotChatService> _logger;

    public BotChatService(
        IBotChatHandler chatHandler,
        IBotChatHealthService chatHealthService,
        IServiceProvider serviceProvider,
        ILogger<BotChatService> logger)
    {
        _chatHandler = chatHandler;
        _chatHealthService = chatHealthService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken ct = default)
    {
        return await _chatHandler.GetChatAsync(chatId, ct);
    }

    public async Task<IReadOnlyList<ChatMember>> GetAdministratorsAsync(
        long chatId,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        // For now, delegate directly to handler
        // TODO: Add caching layer when admin cache is migrated from ChatManagementService
        var admins = await _chatHandler.GetChatAdministratorsAsync(chatId, ct);
        return admins;
    }

    public async Task<string?> GetInviteLinkAsync(long chatId, CancellationToken ct = default)
    {
        try
        {
            // Check database cache first (avoid Telegram API call if cached)
            using var scope = _serviceProvider.CreateScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<IConfigRepository>();

            var cachedConfig = await configRepo.GetByChatIdAsync(chatId, ct);
            if (cachedConfig?.InviteLink != null)
            {
                _logger.LogDebug("Using cached invite link for chat {ChatId}", chatId);
                return cachedConfig.InviteLink;
            }

            // Not cached - fetch and cache
            return await FetchAndCacheInviteLinkAsync(chatId, configRepo, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to get invite link for chat {ChatId}. Bot may lack admin permissions.",
                chatId);
            return null;
        }
    }

    public async Task<string?> RefreshInviteLinkAsync(long chatId, CancellationToken ct = default)
    {
        try
        {
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

    public async Task LeaveChatAsync(long chatId, CancellationToken ct = default)
    {
        await _chatHandler.LeaveChatAsync(chatId, ct);
    }

    public async Task<bool> CheckHealthAsync(long chatId, CancellationToken ct = default)
    {
        try
        {
            // Basic health check - can we get chat info?
            var chat = await _chatHandler.GetChatAsync(chatId, ct);
            return chat != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for chat {ChatId}", chatId);
            return false;
        }
    }

    public IReadOnlyList<long> GetHealthyChatIds()
    {
        return _chatHealthService.GetHealthyChatIds().ToList();
    }

    /// <summary>
    /// Fetch current invite link from Telegram API and cache in database.
    /// Only updates cache if link has changed (reduces unnecessary writes).
    /// </summary>
    private async Task<string?> FetchAndCacheInviteLinkAsync(
        long chatId,
        IConfigRepository configRepo,
        CancellationToken ct)
    {
        var chat = await _chatHandler.GetChatAsync(chatId, ct);
        string? currentLink;

        // Public group - use username link (e.g., https://t.me/groupname)
        if (!string.IsNullOrEmpty(chat.Username))
        {
            currentLink = $"https://t.me/{chat.Username}";
            _logger.LogDebug("Got public invite link for {Chat}: {Link}",
                LogDisplayName.ChatDebug(chat.Title, chatId), currentLink);

            // Cache public group link too (username could change)
            var cachedConfig = await configRepo.GetByChatIdAsync(chatId, ct);
            if (cachedConfig?.InviteLink != currentLink)
            {
                await configRepo.SaveInviteLinkAsync(chatId, currentLink, ct);
                _logger.LogDebug("Cached public invite link for chat {ChatId}", chatId);
            }

            return currentLink;
        }

        // Private group - check if we already have a cached link
        // ExportChatInviteLink GENERATES a new link (revokes old), so we must avoid calling it
        var cachedConfigPrivate = await configRepo.GetByChatIdAsync(chatId, ct);

        if (cachedConfigPrivate?.InviteLink != null)
        {
            // Use cached link - don't call ExportChatInviteLink (it would revoke this one)
            _logger.LogDebug("Using existing cached invite link for private chat {ChatId}", chatId);
            return cachedConfigPrivate.InviteLink;
        }

        // No cached link - export the primary link (this WILL revoke any previous primary link)
        // This should only happen on first setup
        currentLink = await _chatHandler.ExportChatInviteLinkAsync(chatId, ct);
        _logger.LogWarning(
            "Exported PRIMARY invite link for private chat {ChatId} - this revokes previous primary link! Link: {Link}",
            chatId,
            currentLink);

        // Cache it so we never call ExportChatInviteLink again for this chat
        await configRepo.SaveInviteLinkAsync(chatId, currentLink, ct);
        return currentLink;
    }
}
