using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Telegram;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for managing chats, performing health checks, and testing bot permissions
/// </summary>
public class ManagedChatService : IManagedChatService
{
    private readonly IManagedChatsRepository _chatsRepository;
    private readonly IChatAdminsRepository _adminsRepository;
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<ManagedChatService> _logger;

    public ManagedChatService(
        IManagedChatsRepository chatsRepository,
        IChatAdminsRepository adminsRepository,
        TelegramBotClientFactory botFactory,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<ManagedChatService> logger)
    {
        _chatsRepository = chatsRepository;
        _adminsRepository = adminsRepository;
        _botClient = botFactory.GetOrCreate(telegramOptions.Value.BotToken);
        _logger = logger;
    }

    /// <summary>
    /// Get all managed chats with enriched health information
    /// </summary>
    public async Task<List<ManagedChatInfo>> GetAllChatsWithHealthAsync()
    {
        var chats = await _chatsRepository.GetAllChatsAsync();
        var result = new List<ManagedChatInfo>();

        foreach (var chat in chats)
        {
            var health = await PerformHealthCheckAsync(chat.ChatId);
            var hasCustomConfig = await HasCustomSpamConfigAsync(chat.ChatId);

            result.Add(new ManagedChatInfo
            {
                Chat = chat,
                HealthStatus = health,
                HasCustomSpamConfig = hasCustomConfig
            });
        }

        return result;
    }

    /// <summary>
    /// Perform health check on a specific chat
    /// </summary>
    public async Task<ChatHealthStatus> PerformHealthCheckAsync(long chatId)
    {
        var health = new ChatHealthStatus { ChatId = chatId };

        try
        {
            // Try to get chat info from Telegram
            var chat = await _botClient.GetChat(chatId);
            health.IsReachable = true;
            health.ChatTitle = chat.Title;

            // Get bot member info to check permissions
            var botMember = await _botClient.GetChatMember(chatId, _botClient.BotId);
            health.BotStatus = botMember.Status.ToString();
            health.IsAdmin = botMember.Status == ChatMemberStatus.Administrator;

            if (botMember is ChatMemberAdministrator admin)
            {
                health.CanDeleteMessages = admin.CanDeleteMessages;
                health.CanRestrictMembers = admin.CanRestrictMembers;
                health.CanPromoteMembers = admin.CanPromoteMembers;
                health.CanInviteUsers = admin.CanInviteUsers;
            }
            else if (botMember.Status == ChatMemberStatus.Creator)
            {
                // Creator has all permissions
                health.CanDeleteMessages = true;
                health.CanRestrictMembers = true;
                health.CanPromoteMembers = true;
                health.CanInviteUsers = true;
            }

            // Check for warnings
            if (!health.IsAdmin)
            {
                health.Warnings.Add("Bot is not an administrator - moderation features unavailable");
            }
            else
            {
                if (!health.CanDeleteMessages)
                    health.Warnings.Add("Bot cannot delete messages - spam removal unavailable");
                if (!health.CanRestrictMembers)
                    health.Warnings.Add("Bot cannot ban users - automatic bans unavailable");
            }

            // Get admin count
            var admins = await _adminsRepository.GetChatAdminsAsync(chatId);
            health.AdminCount = admins.Count;

            health.Status = health.Warnings.Count == 0 ? "Healthy" : "Warning";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for chat {ChatId}", chatId);
            health.IsReachable = false;
            health.Status = "Error";
            health.Warnings.Add($"Failed to reach chat: {ex.Message}");
        }

        return health;
    }

    /// <summary>
    /// Refresh admin list for a chat by calling GetChatAdministrators
    /// </summary>
    public async Task<int> RefreshChatAdminsAsync(long chatId)
    {
        try
        {
            _logger.LogInformation("Refreshing admin list for chat {ChatId}", chatId);

            // Get all administrators from Telegram
            var admins = await _botClient.GetChatAdministrators(chatId);

            // Upsert each admin
            foreach (var admin in admins)
            {
                var isCreator = admin.Status == ChatMemberStatus.Creator;
                await _adminsRepository.UpsertAsync(chatId, admin.User.Id, isCreator);
            }

            _logger.LogInformation("Refreshed {Count} admins for chat {ChatId}", admins.Length, chatId);
            return admins.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh admins for chat {ChatId}", chatId);
            throw;
        }
    }

    /// <summary>
    /// Test bot permissions in a chat
    /// </summary>
    public async Task<BotPermissionsTest> TestBotPermissionsAsync(long chatId)
    {
        var test = new BotPermissionsTest { ChatId = chatId };

        try
        {
            var botMember = await _botClient.GetChatMember(chatId, _botClient.BotId);
            test.BotStatus = botMember.Status.ToString();
            test.IsAdmin = botMember.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator;

            if (botMember is ChatMemberAdministrator admin)
            {
                test.CanDeleteMessages = admin.CanDeleteMessages;
                test.CanRestrictMembers = admin.CanRestrictMembers;
                test.CanPromoteMembers = admin.CanPromoteMembers;
                test.CanInviteUsers = admin.CanInviteUsers;
                test.CanPinMessages = admin.CanPinMessages;
            }
            else if (botMember.Status == ChatMemberStatus.Creator)
            {
                test.CanDeleteMessages = true;
                test.CanRestrictMembers = true;
                test.CanPromoteMembers = true;
                test.CanInviteUsers = true;
                test.CanPinMessages = true;
            }

            test.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test bot permissions for chat {ChatId}", chatId);
            test.Success = false;
            test.ErrorMessage = ex.Message;
        }

        return test;
    }

    /// <summary>
    /// Leave a chat (bot leaves the group)
    /// </summary>
    public async Task<bool> LeaveChatAsync(long chatId)
    {
        try
        {
            await _botClient.LeaveChat(chatId);
            await _chatsRepository.MarkInactiveAsync(chatId);
            _logger.LogInformation("Bot left chat {ChatId}", chatId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave chat {ChatId}", chatId);
            throw;
        }
    }

    /// <summary>
    /// Check if a chat has custom spam detection configuration
    /// </summary>
    private async Task<bool> HasCustomSpamConfigAsync(long chatId)
    {
        // TODO: Query spam_detection_configs table to check if chat_id exists
        // For now, return false
        return await Task.FromResult(false);
    }
}
