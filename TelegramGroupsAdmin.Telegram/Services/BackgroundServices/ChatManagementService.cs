using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Handles chat management, admin caching, and health checking
/// </summary>
public class ChatManagementService(
    IServiceProvider serviceProvider,
    ILogger<ChatManagementService> logger)
{
    private readonly ConcurrentDictionary<long, ChatHealthStatus> _healthCache = new();

    // Event for real-time UI updates
    public event Action<ChatHealthStatus>? OnHealthUpdate;

    /// <summary>
    /// Get cached health status for a chat (null if not yet checked)
    /// </summary>
    public ChatHealthStatus? GetCachedHealth(long chatId)
        => _healthCache.TryGetValue(chatId, out var health) ? health : null;

    /// <summary>
    /// Handle MyChatMember updates (bot added/removed, admin promotion/demotion)
    /// </summary>
    public async Task HandleMyChatMemberUpdateAsync(ChatMemberUpdated myChatMember)
    {
        try
        {
            var chat = myChatMember.Chat;
            var oldStatus = myChatMember.OldChatMember.Status;
            var newStatus = myChatMember.NewChatMember.Status;
            var isAdmin = newStatus == ChatMemberStatus.Administrator;
            var isActive = newStatus is ChatMemberStatus.Member or ChatMemberStatus.Administrator;

            // Map Telegram ChatType to our ManagedChatType enum
            var chatType = chat.Type switch
            {
                ChatType.Private => ManagedChatType.Private,
                ChatType.Group => ManagedChatType.Group,
                ChatType.Supergroup => ManagedChatType.Supergroup,
                ChatType.Channel => ManagedChatType.Channel,
                _ => ManagedChatType.Group
            };

            // Map Telegram ChatMemberStatus to our BotChatStatus enum
            var botStatus = newStatus switch
            {
                ChatMemberStatus.Member => BotChatStatus.Member,
                ChatMemberStatus.Administrator => BotChatStatus.Administrator,
                ChatMemberStatus.Left => BotChatStatus.Left,
                ChatMemberStatus.Kicked => BotChatStatus.Kicked,
                _ => BotChatStatus.Member
            };

            var chatRecord = new ManagedChatRecord(
                ChatId: chat.Id,
                ChatName: chat.Title ?? chat.Username ?? $"Chat {chat.Id}",
                ChatType: chatType,
                BotStatus: botStatus,
                IsAdmin: isAdmin,
                AddedAt: DateTimeOffset.UtcNow,
                IsActive: isActive,
                LastSeenAt: DateTimeOffset.UtcNow,
                SettingsJson: null
            );

            using (var scope = serviceProvider.CreateScope())
            {
                var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
                await managedChatsRepository.UpsertAsync(chatRecord);
            }

            // If this is about ANOTHER user (not the bot), update admin cache
            var affectedUser = myChatMember.NewChatMember.User;

            if (affectedUser.IsBot == false) // Admin change for a real user
            {
                var wasAdmin = oldStatus is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;
                var isNowAdmin = newStatus is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;

                if (wasAdmin != isNowAdmin)
                {
                    using var scope = serviceProvider.CreateScope();
                    var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

                    if (isNowAdmin)
                    {
                        // User promoted to admin
                        var isCreator = newStatus == ChatMemberStatus.Creator;
                        await chatAdminsRepository.UpsertAsync(chat.Id, affectedUser.Id, isCreator);
                        logger.LogInformation(
                            "✅ User {UserId} (@{Username}) promoted to {Role} in chat {ChatId}",
                            affectedUser.Id,
                            affectedUser.Username ?? "unknown",
                            isCreator ? "creator" : "admin",
                            chat.Id);
                    }
                    else
                    {
                        // User demoted from admin
                        await chatAdminsRepository.DeactivateAsync(chat.Id, affectedUser.Id);
                        logger.LogInformation(
                            "❌ User {UserId} (@{Username}) demoted from admin in chat {ChatId}",
                            affectedUser.Id,
                            affectedUser.Username ?? "unknown",
                            chat.Id);
                    }
                }
            }

            if (isActive)
            {
                logger.LogInformation(
                    "✅ Bot added to {ChatType} {ChatId} ({ChatName}) as {Status}",
                    chat.Type,
                    chat.Id,
                    chat.Title ?? chat.Username ?? "Unknown",
                    newStatus);
            }
            else
            {
                logger.LogWarning(
                    "❌ Bot removed from {ChatId} ({ChatName}) - status: {Status}",
                    chat.Id,
                    chat.Title ?? chat.Username ?? "Unknown",
                    newStatus);
            }

            // Trigger immediate health check when bot status changes
            await RefreshHealthForChatAsync(null, chat.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error handling MyChatMember update for chat {ChatId}",
                myChatMember.Chat.Id);
        }
    }

    /// <summary>
    /// Refresh admin cache for all active managed chats on startup
    /// </summary>
    public async Task RefreshAllChatAdminsAsync(ITelegramBotClient botClient, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
            var managedChats = await managedChatsRepository.GetActiveChatsAsync();

            logger.LogInformation("Refreshing admin cache for {Count} managed chats", managedChats.Count);

            var refreshedCount = 0;
            foreach (var chat in managedChats)
            {
                try
                {
                    await RefreshChatAdminsAsync(botClient, chat.ChatId, cancellationToken);
                    refreshedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to refresh admin cache for chat {ChatId}", chat.ChatId);
                }
            }

            logger.LogInformation("✅ Admin cache refreshed for {Count}/{Total} chats", refreshedCount, managedChats.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh admin cache on startup");
        }
    }

    /// <summary>
    /// Refresh admin list for a specific chat
    /// </summary>
    public async Task RefreshChatAdminsAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        try
        {
            // Get all administrators from Telegram
            var admins = await botClient.GetChatAdministrators(chatId, cancellationToken);

            using var scope = serviceProvider.CreateScope();
            var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

            var adminNames = new List<string>();
            foreach (var admin in admins)
            {
                var isCreator = admin.Status == ChatMemberStatus.Creator;
                var username = admin.User.Username; // Store Telegram username (without @)
                await chatAdminsRepository.UpsertAsync(chatId, admin.User.Id, isCreator, username);

                var displayName = username ?? admin.User.FirstName ?? admin.User.Id.ToString();
                adminNames.Add($"@{displayName}" + (isCreator ? " (creator)" : ""));
            }

            logger.LogInformation(
                "Cached {Count} admins for chat {ChatId}: {Admins}",
                admins.Length,
                chatId,
                string.Join(", ", adminNames));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh admins for chat {ChatId}", chatId);
            throw; // Re-throw so caller can track failures
        }
    }

    /// <summary>
    /// Perform health check on a specific chat and update cache
    /// </summary>
    public async Task RefreshHealthForChatAsync(ITelegramBotClient? botClient, long chatId)
    {
        try
        {
            if (botClient == null)
            {
                logger.LogWarning("Bot client not initialized, skipping health check for chat {ChatId}", chatId);
                return;
            }

            var (health, chatName) = await PerformHealthCheckAsync(botClient, chatId);
            _healthCache[chatId] = health;
            OnHealthUpdate?.Invoke(health);

            // Update chat name in database if we got a valid title from Telegram
            if (health.IsReachable && !string.IsNullOrEmpty(chatName))
            {
                using var scope = serviceProvider.CreateScope();
                var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
                var existingChat = await managedChatsRepository.GetByChatIdAsync(chatId);

                if (existingChat != null && existingChat.ChatName != chatName)
                {
                    // Update with fresh chat name from Telegram
                    var updatedChat = existingChat with { ChatName = chatName };
                    await managedChatsRepository.UpsertAsync(updatedChat);
                    logger.LogDebug("Updated chat name for {ChatId}: {OldName} -> {NewName}",
                        chatId, existingChat.ChatName, chatName);
                }
            }

            logger.LogDebug("Health check completed for chat {ChatId}: {Status}", chatId, health.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to perform health check for chat {ChatId}", chatId);
        }
    }

    /// <summary>
    /// Perform health check on a chat (check reachability, permissions, etc.)
    /// Returns tuple of (health status, chat name)
    /// </summary>
    private async Task<(ChatHealthStatus Health, string? ChatName)> PerformHealthCheckAsync(ITelegramBotClient botClient, long chatId)
    {
        var health = new ChatHealthStatus
        {
            ChatId = chatId,
            IsReachable = false,
            Status = "Unknown"
        };
        string? chatName = null;

        try
        {
            // Try to get chat info
            var chat = await botClient.GetChat(chatId);
            health.IsReachable = true;
            chatName = chat.Title ?? chat.Username ?? $"Chat {chatId}";

            // Get bot's member status
            var botMember = await botClient.GetChatMember(chatId, botClient.BotId);
            health.BotStatus = botMember.Status.ToString();
            health.IsAdmin = botMember.Status == ChatMemberStatus.Administrator;

            // Check permissions if admin
            if (botMember.Status == ChatMemberStatus.Administrator && botMember is ChatMemberAdministrator admin)
            {
                health.CanDeleteMessages = admin.CanDeleteMessages;
                health.CanRestrictMembers = admin.CanRestrictMembers;
                health.CanPromoteMembers = admin.CanPromoteMembers;
                health.CanInviteUsers = admin.CanInviteUsers;
            }

            // Get admin count
            var admins = await botClient.GetChatAdministrators(chatId);
            health.AdminCount = admins.Length;

            // Determine overall status
            health.Status = "Healthy";
            health.Warnings.Clear();

            if (!health.IsAdmin)
            {
                health.Status = "Warning";
                health.Warnings.Add("Bot is not an admin in this chat");
            }
            else
            {
                if (!health.CanDeleteMessages)
                    health.Warnings.Add("Bot cannot delete messages");
                if (!health.CanRestrictMembers)
                    health.Warnings.Add("Bot cannot ban/restrict users");

                if (health.Warnings.Count > 0)
                    health.Status = "Warning";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed for chat {ChatId}", chatId);
            health.IsReachable = false;
            health.Status = "Error";
            health.Warnings.Add($"Cannot reach chat: {ex.Message}");
        }

        return (health, chatName);
    }

    /// <summary>
    /// Refresh health for all managed chats
    /// </summary>
    public async Task RefreshAllHealthAsync(ITelegramBotClient? botClient)
    {
        try
        {
            if (botClient == null)
            {
                logger.LogWarning("Bot client not initialized, skipping health check");
                return;
            }

            using var scope = serviceProvider.CreateScope();
            var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
            var chats = await managedChatsRepository.GetAllChatsAsync();

            foreach (var chat in chats.Where(c => c.IsActive))
            {
                await RefreshHealthForChatAsync(botClient, chat.ChatId);
            }

            logger.LogInformation("Completed health check for {Count} chats", chats.Count(c => c.IsActive));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh health for all chats");
        }
    }
}
