using TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service interface for managing chats, performing health checks, and testing bot permissions
/// </summary>
public interface IManagedChatService
{
    /// <summary>
    /// Get all managed chats with enriched health information
    /// </summary>
    Task<List<ManagedChatInfo>> GetAllChatsWithHealthAsync();

    /// <summary>
    /// Perform health check on a specific chat
    /// </summary>
    Task<ChatHealthStatus> PerformHealthCheckAsync(long chatId);

    /// <summary>
    /// Refresh admin list for a chat by calling GetChatAdministrators
    /// </summary>
    Task<int> RefreshChatAdminsAsync(long chatId);

    /// <summary>
    /// Test bot permissions in a chat
    /// </summary>
    Task<BotPermissionsTest> TestBotPermissionsAsync(long chatId);

    /// <summary>
    /// Leave a chat (bot leaves the group)
    /// </summary>
    Task<bool> LeaveChatAsync(long chatId);
}
