using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Handles chat management, admin caching, and health checking
/// Phase 5.1: Sends ChatHealthWarning notifications to Owners
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
    /// Only tracks groups/supergroups - private chats are not managed
    /// </summary>
    public async Task HandleMyChatMemberUpdateAsync(ChatMemberUpdated myChatMember, CancellationToken cancellationToken = default)
    {
        try
        {
            var chat = myChatMember.Chat;

            // Skip private chats - only manage groups/supergroups
            if (chat.Type == ChatType.Private)
            {
                logger.LogDebug("Skipping MyChatMember update for private chat {ChatId}", chat.Id);
                return;
            }

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
                SettingsJson: null,
                ChatIconPath: null
            );

            using var scope = serviceProvider.CreateScope();
            var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
            await managedChatsRepository.UpsertAsync(chatRecord, cancellationToken);

            // If this is about ANOTHER user (not the bot), update admin cache
            var affectedUser = myChatMember.NewChatMember.User;

            if (affectedUser.IsBot == false) // Admin change for a real user
            {
                var wasAdmin = oldStatus is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;
                var isNowAdmin = newStatus is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;

                if (wasAdmin != isNowAdmin)
                {
                    using var adminScope = serviceProvider.CreateScope();
                    var chatAdminsRepository = adminScope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

                    if (isNowAdmin)
                    {
                        // User promoted to admin
                        var isCreator = newStatus == ChatMemberStatus.Creator;
                        await chatAdminsRepository.UpsertAsync(chat.Id, affectedUser.Id, isCreator, cancellationToken: cancellationToken);
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
                        await chatAdminsRepository.DeactivateAsync(chat.Id, affectedUser.Id, cancellationToken);
                        logger.LogInformation(
                            "❌ User {UserId} (@{Username}) demoted from admin in chat {ChatId}",
                            affectedUser.Id,
                            affectedUser.Username ?? "unknown",
                            chat.Id);
                    }
                }
            }

            // If bot was just promoted to admin, refresh admin cache for this chat
            var botWasAdmin = oldStatus == ChatMemberStatus.Administrator;
            var botIsNowAdmin = newStatus == ChatMemberStatus.Administrator;

            if (!botWasAdmin && botIsNowAdmin)
            {
                logger.LogInformation(
                    "🎉 Bot promoted to admin in {ChatId} ({ChatName}), refreshing admin cache",
                    chat.Id,
                    chat.Title ?? chat.Username ?? "Unknown");

                // Note: Can't call RefreshChatAdminsAsync here because botClient not available
                // Will be refreshed on next message or by background health check
                // TODO: Consider passing botClient to this method for immediate refresh
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
            await RefreshHealthForChatAsync(null, chat.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error handling MyChatMember update for chat {ChatId}",
                myChatMember.Chat.Id);
        }
    }

    /// <summary>
    /// Handle Group → Supergroup migration
    /// When a Group is upgraded to Supergroup (e.g., granting admin), Telegram:
    /// 1. Creates a new Supergroup with different chat ID
    /// 2. Old Group becomes inaccessible (invalid chat ID)
    /// We delete the old chat record since the ID is now invalid
    /// </summary>
    public async Task HandleChatMigrationAsync(long oldChatId, long newChatId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();

            // Delete old chat record (the old chat ID is now invalid)
            var oldChat = await managedChatsRepository.GetByChatIdAsync(oldChatId, cancellationToken);
            if (oldChat != null)
            {
                await managedChatsRepository.DeleteAsync(oldChatId, cancellationToken);

                logger.LogInformation(
                    "Deleted old Group {OldChatId} record (migrated to Supergroup {NewChatId})",
                    oldChatId,
                    newChatId);
            }

            // The new Supergroup will be added automatically via HandleMyChatMemberUpdateAsync
            logger.LogInformation(
                "Chat migration handled: {OldChatId} → {NewChatId}. New chat will be added automatically.",
                oldChatId,
                newChatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to handle chat migration from {OldChatId} to {NewChatId}",
                oldChatId,
                newChatId);
        }
    }

    /// <summary>
    /// Handle ChatMember updates for admin promotion/demotion (instant permission updates)
    /// Called when any user (not just bot) is promoted/demoted in a managed chat
    /// </summary>
    public async Task HandleAdminStatusChangeAsync(ChatMemberUpdated chatMemberUpdate, CancellationToken cancellationToken = default)
    {
        try
        {
            var chat = chatMemberUpdate.Chat;
            var user = chatMemberUpdate.NewChatMember.User;
            var oldStatus = chatMemberUpdate.OldChatMember.Status;
            var newStatus = chatMemberUpdate.NewChatMember.Status;

            // Skip if status didn't involve admin permissions
            var wasAdmin = oldStatus is ChatMemberStatus.Administrator or ChatMemberStatus.Creator;
            var isNowAdmin = newStatus is ChatMemberStatus.Administrator or ChatMemberStatus.Creator;

            if (wasAdmin == isNowAdmin)
            {
                // No admin status change (e.g., just permissions updated, or regular member change)
                return;
            }

            using var scope = serviceProvider.CreateScope();
            var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

            if (isNowAdmin)
            {
                // User promoted to admin
                var isCreator = newStatus == ChatMemberStatus.Creator;
                await chatAdminsRepository.UpsertAsync(chat.Id, user.Id, isCreator, user.Username, cancellationToken);

                logger.LogInformation(
                    "⬆️ INSTANT: User promoted to {Role} in chat {ChatId} ({ChatName}): {TelegramId} (@{Username})",
                    isCreator ? "creator" : "admin",
                    chat.Id,
                    chat.Title ?? "Unknown",
                    user.Id,
                    user.Username ?? "unknown");
            }
            else
            {
                // User demoted from admin
                await chatAdminsRepository.DeactivateAsync(chat.Id, user.Id, cancellationToken);

                logger.LogInformation(
                    "⬇️ INSTANT: User demoted from admin in chat {ChatId} ({ChatName}): {TelegramId} (@{Username})",
                    chat.Id,
                    chat.Title ?? "Unknown",
                    user.Id,
                    user.Username ?? "unknown");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle admin status change for user {UserId} in chat {ChatId}",
                chatMemberUpdate.NewChatMember.User.Id,
                chatMemberUpdate.Chat.Id);
        }
    }

    /// <summary>
    /// Refresh admin cache for all active managed chats on startup
    /// </summary>
    public async Task RefreshAllChatAdminsAsync(ITelegramBotClient botClient, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
            var managedChats = await managedChatsRepository.GetActiveChatsAsync(cancellationToken);

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
    /// Refresh admin list for a specific chat (groups/supergroups only)
    /// </summary>
    public async Task RefreshChatAdminsAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if this is a group chat (only groups/supergroups have administrators)
            var chat = await botClient.GetChat(chatId, cancellationToken);
            if (chat.Type != ChatType.Group && chat.Type != ChatType.Supergroup)
            {
                logger.LogDebug("Skipping admin refresh for non-group chat {ChatId} (type: {Type})", chatId, chat.Type);
                return;
            }

            // Get all administrators from Telegram
            var admins = await botClient.GetChatAdministrators(chatId, cancellationToken);

            using var scope = serviceProvider.CreateScope();
            var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

            // Get current admin IDs from Telegram
            var currentAdminIds = admins.Select(a => a.User.Id).ToHashSet();

            // Get cached admins from database
            var cachedAdmins = await chatAdminsRepository.GetChatAdminsAsync(chatId, cancellationToken);
            var cachedAdminIds = cachedAdmins.Select(a => a.TelegramId).ToHashSet();

            // Find demoted admins (in cache but not in current list)
            var demotedAdminIds = cachedAdminIds.Except(currentAdminIds).ToList();

            // Deactivate demoted admins
            foreach (var demotedId in demotedAdminIds)
            {
                await chatAdminsRepository.DeactivateAsync(chatId, demotedId, cancellationToken);
                var demotedAdmin = cachedAdmins.First(a => a.TelegramId == demotedId);
                logger.LogInformation(
                    "⬇️ Admin demoted in chat {ChatId}: {TelegramId} (@{Username})",
                    chatId,
                    demotedId,
                    demotedAdmin.Username ?? "unknown");
            }

            // Upsert current admins (add new, update existing)
            var adminNames = new List<string>();
            foreach (var admin in admins)
            {
                var isCreator = admin.Status == ChatMemberStatus.Creator;
                var username = admin.User.Username; // Store Telegram username (without @)
                var wasNew = !cachedAdminIds.Contains(admin.User.Id);

                await chatAdminsRepository.UpsertAsync(chatId, admin.User.Id, isCreator, username, cancellationToken);

                var displayName = username ?? admin.User.FirstName ?? admin.User.Id.ToString();
                adminNames.Add($"@{displayName}" + (isCreator ? " (creator)" : ""));

                if (wasNew)
                {
                    logger.LogInformation(
                        "⬆️ New admin promoted in chat {ChatId}: {TelegramId} (@{Username})",
                        chatId,
                        admin.User.Id,
                        username ?? "unknown");
                }
            }

            logger.LogInformation(
                "✅ Synced {Count} admins for chat {ChatId}: {Admins}",
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
    public async Task RefreshHealthForChatAsync(ITelegramBotClient? botClient, long chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (botClient == null)
            {
                logger.LogWarning("Bot client not initialized, skipping health check for chat {ChatId}", chatId);
                return;
            }

            var (health, chatName) = await PerformHealthCheckAsync(botClient, chatId, cancellationToken);
            _healthCache[chatId] = health;
            OnHealthUpdate?.Invoke(health);

            // Update chat name in database if we got a valid title from Telegram
            if (health.IsReachable && !string.IsNullOrEmpty(chatName))
            {
                using var scope = serviceProvider.CreateScope();
                var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
                var existingChat = await managedChatsRepository.GetByChatIdAsync(chatId, cancellationToken);

                if (existingChat != null && existingChat.ChatName != chatName)
                {
                    // Update with fresh chat name from Telegram
                    var updatedChat = existingChat with { ChatName = chatName };
                    await managedChatsRepository.UpsertAsync(updatedChat, cancellationToken);
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
    private async Task<(ChatHealthStatus Health, string? ChatName)> PerformHealthCheckAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken = default)
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
            var chat = await botClient.GetChat(chatId, cancellationToken);
            health.IsReachable = true;
            chatName = chat.Title ?? chat.Username ?? $"Chat {chatId}";

            // Skip health check for private chats (only relevant for groups)
            if (chat.Type == ChatType.Private)
            {
                health.Status = "N/A";
                health.Warnings.Add("Health checks not applicable to private chats");
                logger.LogDebug("Skipping health check for private chat {ChatId}", chatId);
                return (health, chatName);
            }

            // Get bot's member status
            var botMember = await botClient.GetChatMember(chatId, botClient.BotId, cancellationToken);
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

            // Get admin count and refresh admin cache (only for groups/supergroups)
            var admins = await botClient.GetChatAdministrators(chatId, cancellationToken);
            health.AdminCount = admins.Length;

            // Refresh admin cache in database (for permission checks in commands)
            await RefreshChatAdminsAsync(botClient, chatId, cancellationToken);

            // Validate and refresh cached invite link (private groups only)
            if (chat.Type == ChatType.Supergroup && string.IsNullOrEmpty(chat.Username))
            {
                await ValidateInviteLinkAsync(botClient, chatId, cancellationToken);
            }

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

            // Send notification if health warnings detected (Phase 5.1)
            if (health.Status == "Warning" || health.Status == "Error")
            {
                var warningsText = string.Join("\n- ", health.Warnings);

                // Create scope to resolve scoped INotificationService from singleton
                using var scope = serviceProvider.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                _ = notificationService.SendSystemNotificationAsync(
                    eventType: NotificationEventType.ChatHealthWarning,
                    subject: $"Chat Health Warning: {chatName ?? chatId.ToString()}",
                    message: $"Health check detected issues with chat '{chatName ?? chatId.ToString()}'.\n\n" +
                             $"Status: {health.Status}\n" +
                             $"Bot Admin: {health.IsAdmin}\n" +
                             $"Warnings:\n- {warningsText}\n\n" +
                             $"Please review the chat settings and bot permissions.",
                    ct: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed for chat {ChatId}", chatId);
            health.IsReachable = false;
            health.Status = "Error";
            health.Warnings.Add($"Cannot reach chat: {ex.Message}");

            // Notify about critical health failure (Phase 5.1)
            // Create scope to resolve scoped INotificationService from singleton
            using var scope = serviceProvider.CreateScope();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            _ = notificationService.SendSystemNotificationAsync(
                eventType: NotificationEventType.ChatHealthWarning,
                subject: $"Chat Health Check Failed: {chatName ?? chatId.ToString()}",
                message: $"Critical: Health check failed for chat '{chatName ?? chatId.ToString()}'.\n\n" +
                         $"Error: {ex.Message}\n\n" +
                         $"The bot may have been removed from the chat or lost permissions.",
                ct: cancellationToken);
        }

        return (health, chatName);
    }

    /// <summary>
    /// Refresh health for all managed chats
    /// </summary>
    public async Task RefreshAllHealthAsync(ITelegramBotClient? botClient, CancellationToken cancellationToken = default)
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
            var chats = await managedChatsRepository.GetAllChatsAsync(cancellationToken);

            foreach (var chat in chats.Where(c => c.IsActive))
            {
                await RefreshHealthForChatAsync(botClient, chat.ChatId, cancellationToken);
            }

            logger.LogInformation("Completed health check for {Count} chats", chats.Count(c => c.IsActive));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh health for all chats");
        }
    }

    /// <summary>
    /// Refresh a single chat (for manual UI refresh button)
    /// Includes admin list, health check, and optionally chat icon
    /// </summary>
    public async Task RefreshSingleChatAsync(
        ITelegramBotClient botClient,
        long chatId,
        bool includeIcon = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Refreshing single chat {ChatId}", chatId);

            // Refresh admin list and health
            await RefreshChatAdminsAsync(botClient, chatId, cancellationToken);
            await RefreshHealthForChatAsync(botClient, chatId, cancellationToken);

            // Optionally fetch fresh chat icon
            if (includeIcon)
            {
                await FetchChatIconAsync(botClient, chatId, cancellationToken);
            }

            logger.LogInformation("✅ Single chat refresh completed for {ChatId}", chatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh single chat {ChatId}", chatId);
            throw;
        }
    }

    /// <summary>
    /// Fetch and cache chat icon (profile photo)
    /// Only called on bot join or manual refresh
    /// Extracted from periodic health check to reduce API calls
    /// </summary>
    private async Task FetchChatIconAsync(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var photoService = scope.ServiceProvider.GetRequiredService<TelegramPhotoService>();
            var iconPath = await photoService.GetChatIconAsync(botClient, chatId);

            if (iconPath != null)
            {
                // Save icon path to database
                var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
                var existingChat = await managedChatsRepository.GetByChatIdAsync(chatId, cancellationToken);

                if (existingChat != null)
                {
                    var updatedChat = existingChat with { ChatIconPath = iconPath };
                    await managedChatsRepository.UpsertAsync(updatedChat, cancellationToken);
                    logger.LogInformation("✅ Cached chat icon for {ChatId}: {IconPath}", chatId, iconPath);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch chat icon for {ChatId} (non-fatal)", chatId);
            // Non-fatal - don't throw
        }
    }

    /// <summary>
    /// Validate and update cached invite link from Telegram
    /// Only applies to private supergroups (not public chats with usernames)
    /// Fetches current primary link from Telegram and updates cache if changed
    /// Detects when admin changes/revokes link and keeps cache in sync
    /// </summary>
    private async Task ValidateInviteLinkAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var inviteLinkService = scope.ServiceProvider.GetRequiredService<IChatInviteLinkService>();

            // Refresh from Telegram API to validate and update if needed
            // This fetches the current primary link and only writes to DB if it changed
            var inviteLink = await inviteLinkService.RefreshInviteLinkAsync(botClient, chatId, cancellationToken);

            if (inviteLink != null)
            {
                logger.LogDebug("Validated invite link for chat {ChatId}", chatId);
            }
            else
            {
                logger.LogWarning("Could not validate invite link for chat {ChatId} - bot may lack admin permissions", chatId);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal - invite link validation failure shouldn't fail health check
            logger.LogWarning(ex, "Failed to validate invite link for chat {ChatId}", chatId);
        }
    }
}
