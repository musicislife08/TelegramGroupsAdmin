using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Telegram;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Orchestrates chat health refresh operations.
/// Scoped service that performs health checks and updates IChatHealthCache.
/// For reading cached health state, inject IChatHealthCache directly.
/// </summary>
public class ChatHealthRefreshOrchestrator(
    ITelegramConfigLoader configLoader,
    IChatCache chatCache,
    IChatHealthCache healthCache,
    IManagedChatsRepository managedChatsRepository,
    ILinkedChannelsRepository linkedChannelsRepository,
    IBotChatHandler chatHandler,
    IBotUserHandler userHandler,
    IBotUserService userService,
    IBotChatService chatService,
    TelegramPhotoService photoService,
    IPhotoHashService photoHashService,
    INotificationService notificationService,
    ILogger<ChatHealthRefreshOrchestrator> logger) : IChatHealthRefreshOrchestrator
{
    /// <summary>
    /// Perform health check on a specific chat and update cache
    /// </summary>
    public async Task RefreshHealthForChatAsync(long chatId, CancellationToken cancellationToken = default)
    {
        string? chatName = null; // Captured early for error logging
        try
        {
            var (health, chatName_) = await PerformHealthCheckAsync(chatId, cancellationToken);
            chatName = chatName_;
            healthCache.SetHealth(chatId, health);

            // Update chat name in database if we got a valid title from Telegram
            if (health.IsReachable && !string.IsNullOrEmpty(chatName))
            {
                var existingChat = await managedChatsRepository.GetByChatIdAsync(chatId, cancellationToken);

                if (existingChat != null && existingChat.ChatName != chatName)
                {
                    // Update with fresh chat name from Telegram
                    var updatedChat = existingChat with { ChatName = chatName };
                    await managedChatsRepository.UpsertAsync(updatedChat, cancellationToken);
                    logger.LogDebug("Updated chat name: {OldName} -> {NewName} ({ChatId})",
                        existingChat.ChatName, chatName, chatId);
                }
            }

            logger.LogDebug("Health check completed for {Chat}: {Status}",
                LogDisplayName.ChatDebug(chatName, chatId), health.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to perform health check for {Chat}",
                LogDisplayName.ChatDebug(chatName, chatId));
        }
    }

    /// <summary>
    /// Perform health check on a chat (check reachability, permissions, etc.)
    /// Returns tuple of (health status, chat name)
    /// </summary>
    private async Task<(ChatHealthStatus Health, string? ChatName)> PerformHealthCheckAsync(long chatId, CancellationToken cancellationToken = default)
    {
        var health = new ChatHealthStatus
        {
            ChatId = chatId,
            IsReachable = false,
            Status = ChatHealthStatusType.Unknown
        };
        string? chatName = null;

        try
        {
            // Try to get chat info
            var chat = await chatHandler.GetChatAsync(chatId, cancellationToken);
            health.IsReachable = true;
            chatName = chat.Title ?? chat.Username ?? $"Chat {chatId}";

            // Cache SDK Chat for use by other services (e.g., NotificationHandler)
            chatCache.UpdateChat(chat);

            // Skip health check for private chats (only relevant for groups)
            if (chat.Type == ChatType.Private)
            {
                health.Status = ChatHealthStatusType.NotApplicable;
                health.Warnings.Add("Health checks not applicable to private chats");
                logger.LogDebug("Skipping health check for private chat {Chat}",
                    LogDisplayName.ChatDebug(chatName, chatId));
                return (health, chatName);
            }

            // Get bot's member status (may fail if chat has hidden members and bot isn't admin)
            try
            {
                var botId = await userService.GetBotIdAsync(cancellationToken);
                var botMember = await userHandler.GetChatMemberAsync(chatId, botId, cancellationToken);
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
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("hidden"))
            {
                // Hidden members enabled and bot isn't admin - can't check own status
                logger.LogDebug("{Chat} has hidden members and bot is not admin",
                    LogDisplayName.ChatDebug(chatName, chatId));
                health.BotStatus = "Unknown (hidden members)";
                health.IsAdmin = false;
            }

            // Get admin count and refresh admin cache (only if bot is admin - these APIs require admin privileges)
            if (health.IsAdmin)
            {
                var admins = await chatHandler.GetChatAdministratorsAsync(chatId, cancellationToken);
                health.AdminCount = admins.Length;

                // Refresh admin cache in database (for permission checks in commands)
                await chatService.RefreshChatAdminsAsync(chatId, cancellationToken);

                // Validate and refresh cached invite link (all groups - public and private)
                if (chat.Type is ChatType.Supergroup or ChatType.Group)
                {
                    await ValidateInviteLinkAsync(chat, cancellationToken);
                }
            }
            else
            {
                health.AdminCount = 0;
            }

            // Determine overall status
            health.Status = ChatHealthStatusType.Healthy;
            health.Warnings.Clear();

            if (!health.IsAdmin)
            {
                health.Status = ChatHealthStatusType.Warning;
                health.Warnings.Add("Bot is not an admin in this chat");
            }
            else
            {
                if (!health.CanDeleteMessages)
                    health.Warnings.Add("Bot cannot delete messages");
                if (!health.CanRestrictMembers)
                    health.Warnings.Add("Bot cannot ban/restrict users");

                if (health.Warnings.Count > 0)
                    health.Status = ChatHealthStatusType.Warning;
            }

            // Send notification if health warnings detected (Phase 5.1)
            if (health.Status is ChatHealthStatusType.Warning or ChatHealthStatusType.Error)
            {
                var warningsText = string.Join("\n- ", health.Warnings);

                _ = notificationService.SendSystemNotificationAsync(
                    eventType: NotificationEventType.ChatHealthWarning,
                    subject: $"Chat Health Warning: {chatName ?? chatId.ToString()}",
                    message: $"Health check detected issues with chat '{chatName ?? chatId.ToString()}'.\n\n" +
                             $"Status: {health.Status}\n" +
                             $"Bot Admin: {health.IsAdmin}\n" +
                             $"Warnings:\n- {warningsText}\n\n" +
                             $"Please review the chat settings and bot permissions.",
                    cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException cancelEx)
        {
            // Timeout or cancellation - transient error, don't send notification
            // Includes TaskCanceledException from HTTP client timeouts
            logger.LogWarning(cancelEx, "Request timeout/cancellation during health check for {Chat} - will retry on next cycle",
                LogDisplayName.ChatDebug(chatName, chatId));
            health.IsReachable = false;
            health.Status = ChatHealthStatusType.Error;
            health.Warnings.Add($"Request timeout: {cancelEx.Message}");
        }
        catch (HttpRequestException httpEx)
        {
            // Transient network error - log warning but don't send notification
            // These are usually temporary API connectivity issues that resolve on their own
            logger.LogWarning(httpEx, "Transient network error during health check for {Chat} - will retry on next cycle",
                LogDisplayName.ChatDebug(chatName, chatId));
            health.IsReachable = false;
            health.Status = ChatHealthStatusType.Error;
            health.Warnings.Add($"Temporary network issue: {httpEx.Message}");
        }
        catch (Exception ex)
        {
            // Check if this is a transient error before sending notification
            var isTransient = IsTransientNetworkError(ex);

            if (isTransient)
            {
                logger.LogWarning(ex, "Transient network error during health check for {Chat} - will retry on next cycle",
                    LogDisplayName.ChatDebug(chatName, chatId));
                health.IsReachable = false;
                health.Status = ChatHealthStatusType.Error;
                health.Warnings.Add($"Temporary network issue: {ex.Message}");
            }
            else
            {
                // Critical error - likely bot kicked or permission denied
                logger.LogError(ex, "Health check failed for {Chat}",
                    LogDisplayName.ChatDebug(chatName, chatId));
                health.IsReachable = false;
                health.Status = ChatHealthStatusType.Error;
                health.Warnings.Add($"Cannot reach chat: {ex.Message}");

                // Notify about critical health failure (Phase 5.1)
                // Only send notifications for non-transient errors (e.g., bot kicked, permission issues)
                _ = notificationService.SendSystemNotificationAsync(
                    eventType: NotificationEventType.ChatHealthWarning,
                    subject: $"Chat Health Check Failed: {chatName ?? chatId.ToString()}",
                    message: $"Critical: Health check failed for chat '{chatName ?? chatId.ToString()}'.\n\n" +
                             $"Error: {ex.Message}\n\n" +
                             $"The bot may have been removed from the chat or lost permissions.",
                    cancellationToken: cancellationToken);
            }
        }

        return (health, chatName);
    }

    /// <summary>
    /// Refresh health for all active managed chats (excludes chats where bot was removed)
    /// Also backfills missing chat icons to ensure they're fetched eventually
    /// </summary>
    public async Task RefreshAllHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if bot is configured
            var botToken = await configLoader.LoadConfigAsync();
            if (string.IsNullOrEmpty(botToken))
            {
                logger.LogWarning("No bot token configured, skipping health check");
                return;
            }

            // Check all non-deleted chats (active + inactive) for health status
            var chats = await managedChatsRepository.GetAllChatsAsync(cancellationToken: cancellationToken);

            foreach (var chat in chats)
            {
                await RefreshHealthForChatAsync(chat.ChatId, cancellationToken);
            }

            logger.LogDebug("Completed health check for {Count} chats", chats.Count);

            // Backfill missing chat icons (only fetches for chats without icons)
            var chatsWithoutIcons = chats.Where(c => string.IsNullOrEmpty(c.ChatIconPath)).ToList();
            if (chatsWithoutIcons.Count > 0)
            {
                logger.LogInformation("Backfilling {Count} missing chat icons", chatsWithoutIcons.Count);
                foreach (var chat in chatsWithoutIcons)
                {
                    await FetchChatIconAsync(chat, cancellationToken);
                }
            }

            // Sync linked channels for all managed chats (impersonation detection)
            // This handles: new links, changed links, and removed links
            logger.LogInformation("Syncing linked channels for {Count} managed chats", chats.Count);
            foreach (var chat in chats)
            {
                await FetchLinkedChannelAsync(chat.ChatId, cancellationToken);
            }
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
        long chatId,
        bool includeIcon = true,
        CancellationToken cancellationToken = default)
    {
        // Fetch chat from DB for logging context and icon update
        ManagedChatRecord? chat = null;
        try
        {
            chat = await managedChatsRepository.GetByChatIdAsync(chatId, cancellationToken);
        }
        catch
        {
            // Non-fatal - continue with ID-only logging if DB fetch fails
        }

        try
        {
            logger.LogDebug("Refreshing single chat {Chat}",
                LogDisplayName.ChatDebug(chat?.ChatName, chatId));

            // Refresh admin list
            await chatService.RefreshChatAdminsAsync(chatId, cancellationToken);

            // Refresh health check
            await RefreshHealthForChatAsync(chatId, cancellationToken);

            // Optionally fetch fresh chat icon
            if (includeIcon && chat != null)
            {
                await FetchChatIconAsync(chat, cancellationToken);
            }

            logger.LogDebug("✅ Single chat refresh completed for {Chat}",
                LogDisplayName.ChatDebug(chat?.ChatName, chatId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh single chat {Chat}",
                LogDisplayName.ChatDebug(chat?.ChatName, chatId));
            throw;
        }
    }

    /// <summary>
    /// Fetch and cache chat icon (profile photo)
    /// Only called on bot join or manual refresh
    /// Extracted from periodic health check to reduce API calls
    /// </summary>
    private async Task FetchChatIconAsync(
        ManagedChatRecord chat,
        CancellationToken cancellationToken = default)
    {
        var (chatId, chatName) = (chat.ChatId, chat.ChatName);
        try
        {
            var iconPath = await photoService.GetChatIconAsync(chatId);

            if (iconPath != null)
            {
                // Save icon path to database
                var updatedChat = chat with { ChatIconPath = iconPath };
                await managedChatsRepository.UpsertAsync(updatedChat, cancellationToken);
                logger.LogInformation("✅ Cached chat icon for {Chat}",
                    LogDisplayName.ChatInfo(chatName, chatId));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch chat icon for {Chat} (non-fatal)",
                LogDisplayName.ChatDebug(chatName, chatId));
            // Non-fatal - don't throw
        }
    }

    /// <summary>
    /// Validate and update cached invite link from Telegram
    /// Applies to all groups (public and private)
    /// - Public groups: Caches https://t.me/{username} link
    /// - Private groups: Fetches/caches primary invite link (only exports if not cached)
    /// </summary>
    private async Task ValidateInviteLinkAsync(Chat chat, CancellationToken cancellationToken = default)
    {
        try
        {
            // Refresh from Telegram API to validate and update if needed
            // This fetches the current primary link and only writes to DB if it changed
            var inviteLink = await chatService.RefreshInviteLinkAsync(chat.Id, cancellationToken);

            if (inviteLink != null)
            {
                logger.LogDebug("Validated invite link for {Chat}", chat.ToLogDebug());
            }
            else
            {
                logger.LogWarning("Could not validate invite link for {Chat} - bot may lack admin permissions",
                    chat.ToLogDebug());
            }
        }
        catch (Exception ex)
        {
            // Non-fatal - invite link validation failure shouldn't fail health check
            logger.LogWarning(ex, "Failed to validate invite link for {Chat}", chat.ToLogDebug());
        }
    }

    /// <summary>
    /// Fetch and sync linked channel information for a managed chat.
    /// Linked channels are used for impersonation detection (comparing user names/photos against channel).
    /// Creates/updates the linked channel record if chat has one, deletes stale record if channel was unlinked.
    /// </summary>
    private async Task FetchLinkedChannelAsync(
        long chatId,
        CancellationToken cancellationToken = default)
    {
        ChatFullInfo? chat = null; // Captured early for error logging
        try
        {
            // Get chat info to check for linked channel
            chat = await chatHandler.GetChatAsync(chatId, cancellationToken);

            if (chat.LinkedChatId.HasValue)
            {
                // Chat has a linked channel - fetch channel info
                var linkedChannelId = chat.LinkedChatId.Value;

                try
                {
                    var linkedChannel = await chatHandler.GetChatAsync(linkedChannelId, cancellationToken);

                    // Fetch and save channel icon
                    string? iconPath = null;
                    try
                    {
                        iconPath = await photoService.GetChatIconAsync(linkedChannelId);
                    }
                    catch (Exception iconEx)
                    {
                        logger.LogWarning(iconEx, "Failed to fetch channel icon for {Channel} (non-fatal)",
                            linkedChannel.ToLogDebug());
                    }

                    // Compute photo hash for impersonation comparison
                    byte[]? photoHash = null;
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        try
                        {
                            photoHash = await photoHashService.ComputePhotoHashAsync(iconPath);
                        }
                        catch (Exception hashEx)
                        {
                            logger.LogWarning(hashEx, "Failed to compute photo hash for {Channel} (non-fatal)",
                                linkedChannel.ToLogDebug());
                        }
                    }

                    // Upsert linked channel record
                    var record = new LinkedChannelRecord(
                        Id: 0, // DB will assign or upsert will match existing
                        ManagedChatId: chatId,
                        ChannelId: linkedChannelId,
                        ChannelName: linkedChannel.Title,
                        ChannelIconPath: iconPath,
                        PhotoHash: photoHash,
                        LastSynced: DateTimeOffset.UtcNow
                    );

                    await linkedChannelsRepository.UpsertAsync(record, cancellationToken);

                    logger.LogDebug(
                        "Synced linked channel {Channel} for {Chat}",
                        LogDisplayName.ChatDebug(linkedChannel.Title, linkedChannelId),
                        LogDisplayName.ChatDebug(chat.Title, chatId));
                }
                catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 403)
                {
                    // Expected: bot is not a member of a private linked channel
                    // Log at Debug level - this is normal, not an error condition
                    logger.LogDebug(
                        "Linked channel {ChannelId} for {Chat} is inaccessible (private channel, bot not a member)",
                        linkedChannelId,
                        LogDisplayName.ChatDebug(chat.Title, chatId));
                }
                catch (Exception channelEx)
                {
                    // Unexpected error - log at Warning level
                    logger.LogWarning(channelEx,
                        "Failed to fetch linked channel {ChannelId} for {Chat}",
                        linkedChannelId,
                        LogDisplayName.ChatDebug(chat.Title, chatId));
                }
            }
            else
            {
                // No linked channel - remove any stale record
                await linkedChannelsRepository.DeleteByChatIdAsync(chatId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync linked channel for {Chat} (non-fatal)", chat.ToLogDebug());
            // Non-fatal - don't throw
        }
    }

    /// <summary>
    /// Detect if an exception is a transient network error that shouldn't trigger alerts
    /// Checks exception type, message patterns, and inner exceptions
    /// </summary>
    private static bool IsTransientNetworkError(Exception ex)
    {
        // Check exception type name for known transient types
        var typeName = ex.GetType().Name;
        if (typeName.Contains("RequestException") ||
            typeName.Contains("ApiRequestException") ||
            typeName.Contains("HttpException"))
        {
            // Telegram.Bot library exceptions - check if network-related
            var message = ex.Message;
            if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("An error occurred while sending", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connection", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Check inner exception for HttpRequestException or OperationCanceledException
        if (ex.InnerException != null)
        {
            if (ex.InnerException is HttpRequestException or OperationCanceledException)
            {
                return true;
            }

            // Recursively check inner exception
            return IsTransientNetworkError(ex.InnerException);
        }

        return false;
    }
}
