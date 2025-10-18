using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for retrieving chat invite links from Telegram API
/// No caching - retrieves fresh link on each request
/// </summary>
public interface IChatInviteLinkService
{
    /// <summary>
    /// Get invite link for chat from Telegram API (not cached)
    /// Returns null if bot lacks permissions or chat is private without link
    /// </summary>
    Task<string?> GetInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation of chat invite link retrieval
/// Used by: /tempban, appeal system (future), welcome system (future)
/// </summary>
public class ChatInviteLinkService : IChatInviteLinkService
{
    private readonly ILogger<ChatInviteLinkService> _logger;

    public ChatInviteLinkService(ILogger<ChatInviteLinkService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> GetInviteLinkAsync(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken ct = default)
    {
        try
        {
            // Get chat info
            var chat = await botClient.GetChat(chatId, ct);

            // Public group - use username link (e.g., https://t.me/groupname)
            if (!string.IsNullOrEmpty(chat.Username))
            {
                var link = $"https://t.me/{chat.Username}";
                _logger.LogDebug("Got public invite link for chat {ChatId}: {Link}", chatId, link);
                return link;
            }

            // Private group - export invite link (requires bot to be admin)
            // Returns format: https://t.me/joinchat/XXXXX or https://t.me/+XXXXX
            var inviteLink = await botClient.ExportChatInviteLink(chatId, ct);
            _logger.LogDebug("Exported invite link for private chat {ChatId}", chatId);
            return inviteLink;
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
}
