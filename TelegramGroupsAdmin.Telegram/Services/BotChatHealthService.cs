using Microsoft.Extensions.DependencyInjection;
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
/// Service for chat health monitoring, admin caching, and bot status tracking.
/// Singleton service that orchestrates health checks and admin cache management.
/// Uses IChatHealthCache for pure state storage.
/// Creates scopes to resolve IBotChatHandler and IBotUserHandler for Telegram API calls.
/// </summary>
public class BotChatHealthService(
    IServiceProvider serviceProvider,
    ITelegramConfigLoader configLoader,
    IChatCache chatCache,
    IChatHealthCache healthCache,
    ILogger<BotChatHealthService> logger) : IBotChatHealthService
{
    // Delegate event to IChatHealthCache
    public event Action<ChatHealthStatus>?  OnHealthUpdate
    {
        add => healthCache.OnHealthUpdate += value;
        remove => healthCache.OnHealthUpdate -= value;
    }

    /// <summary>
    /// Get cached health status for a chat (null if not yet checked).
    /// Delegates to IChatHealthCache.
    /// </summary>
    public ChatHealthStatus? GetCachedHealth(long chatId)
        => healthCache.GetCachedHealth(chatId);

    /// <summary>
    /// Get set of chat IDs where bot has healthy status (admin + required permissions).
    /// Delegates to IChatHealthCache.
    /// </summary>
    public HashSet<long> GetHealthyChatIds()
        => healthCache.GetHealthyChatIds();

    /// <summary>
    /// Filters a list of chat IDs to only include healthy chats (bot has admin permissions).
    /// Delegates to IChatHealthCache.
    /// </summary>
    public List<long> FilterHealthyChats(IEnumerable<long> chatIds)
        => healthCache.FilterHealthyChats(chatIds);

    /// <summary>
    /// Refresh admin cache for all active managed chats on startup
    /// </summary>
    public async Task RefreshAllChatAdminsAsync(CancellationToken cancellationToken = default)
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
                    await RefreshChatAdminsAsync(chat.ChatId, cancellationToken);
                    refreshedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to refresh admin cache for {Chat}",
                        LogDisplayName.ChatDebug(chat.ChatName, chat.ChatId));
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
    public async Task RefreshChatAdminsAsync(long chatId, CancellationToken cancellationToken = default)
    {
        Chat? chat = null; // Captured early for error logging
        try
        {
            // Create scope for handler resolution (singleton service needs scoped handlers)
            using var scope = serviceProvider.CreateScope();
            var chatHandler = scope.ServiceProvider.GetRequiredService<IBotChatHandler>();

            // Check if this is a group chat (only groups/supergroups have administrators)
            chat = await chatHandler.GetChatAsync(chatId, cancellationToken);
            if (chat.Type != ChatType.Group && chat.Type != ChatType.Supergroup)
            {
                logger.LogDebug("Skipping admin refresh for non-group chat {Chat} (type: {Type})",
                    LogDisplayName.ChatDebug(chat.Title, chatId), chat.Type);
                return;
            }

            // Get all administrators from Telegram
            var admins = await chatHandler.GetChatAdministratorsAsync(chatId, cancellationToken);
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
                    "⬇️ {Admin} demoted in {Chat}",
                    demotedAdmin.DisplayName,
                    LogDisplayName.ChatInfo(chat.Title, chatId));
            }

            // Upsert current admins (add new, update existing)
            var adminNames = new List<string>();
            var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

            foreach (var admin in admins)
            {
                // Ensure user exists in telegram_users first (FK constraint)
                await EnsureUserExistsAsync(userRepo, admin.User, cancellationToken);

                var isCreator = admin.Status == ChatMemberStatus.Creator;
                var wasNew = !cachedAdminIds.Contains(admin.User.Id);

                await chatAdminsRepository.UpsertAsync(chatId, admin.User.Id, isCreator, cancellationToken);

                var displayName = TelegramDisplayName.Format(admin.User.FirstName, admin.User.LastName, admin.User.Username, admin.User.Id);
                adminNames.Add(displayName + (isCreator ? " (creator)" : ""));

                if (wasNew)
                {
                    logger.LogInformation(
                        "⬆️ New admin {Admin} promoted in {Chat}",
                        LogDisplayName.UserInfo(admin.User.FirstName, admin.User.LastName, admin.User.Username, admin.User.Id),
                        LogDisplayName.ChatInfo(chat.Title, chatId));

                    // AUTO-TRUST: Trust new admins globally
                    try
                    {
                        var userActionsRepo = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();

                        var trustAction = new UserActionRecord(
                            Id: 0,
                            UserId: admin.User.Id,
                            ActionType: UserActionType.Trust,
                            MessageId: null,
                            IssuedBy: Actor.AutoTrust,
                            IssuedAt: DateTimeOffset.UtcNow,
                            ExpiresAt: null,
                            Reason: $"Admin in chat {chatId}"
                        );

                        await userActionsRepo.InsertAsync(trustAction, cancellationToken);
                        await userRepo.UpdateTrustStatusAsync(admin.User.Id, isTrusted: true, cancellationToken);

                        logger.LogInformation(
                            "Auto-trusted {User} - admin in {Chat}",
                            LogDisplayName.UserInfo(admin.User.FirstName, admin.User.LastName, admin.User.Username, admin.User.Id),
                            LogDisplayName.ChatInfo(chat.Title, chatId));
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to auto-trust admin {User} in {Chat}",
                            LogDisplayName.UserDebug(admin.User.FirstName, admin.User.LastName, admin.User.Username, admin.User.Id),
                            LogDisplayName.ChatDebug(chat.Title, chatId));
                    }
                }
            }

            logger.LogDebug(
                "✅ Synced {Count} admins for {Chat}: {Admins}",
                admins.Length,
                LogDisplayName.ChatDebug(chat.Title, chatId),
                string.Join(", ", adminNames));

        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh admins for {Chat}", chat.ToLogDebug());
            throw; // Re-throw so caller can track failures
        }
    }

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
                using var scope = serviceProvider.CreateScope();
                var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
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

        // Create scope for handler/service resolution (singleton service needs scoped handlers)
        using var handlerScope = serviceProvider.CreateScope();
        var chatHandler = handlerScope.ServiceProvider.GetRequiredService<IBotChatHandler>();
        var userHandler = handlerScope.ServiceProvider.GetRequiredService<IBotUserHandler>();
        var userService = handlerScope.ServiceProvider.GetRequiredService<IBotUserService>();

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
                await RefreshChatAdminsAsync(chatId, cancellationToken);

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
                // Create scope to resolve scoped INotificationService from singleton
                using var scope = serviceProvider.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

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

            using var scope = serviceProvider.CreateScope();
            var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
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
            using var scope = serviceProvider.CreateScope();
            var managedChatsRepo = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
            chat = await managedChatsRepo.GetByChatIdAsync(chatId, cancellationToken);
        }
        catch
        {
            // Non-fatal - continue with ID-only logging if DB fetch fails
        }

        try
        {
            logger.LogDebug("Refreshing single chat {Chat}",
                LogDisplayName.ChatDebug(chat?.ChatName, chatId));

            // Refresh admin list and health
            await RefreshChatAdminsAsync(chatId, cancellationToken);
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
            using var scope = serviceProvider.CreateScope();
            var photoService = scope.ServiceProvider.GetRequiredService<TelegramPhotoService>();
            var iconPath = await photoService.GetChatIconAsync(chatId);

            if (iconPath != null)
            {
                // Save icon path to database
                var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
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
            using var scope = serviceProvider.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IBotChatService>();

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
    /// Ensures a Telegram user exists in the database before creating dependent records (e.g., chat_admins).
    /// Creates a minimal user record if the user doesn't exist yet (they may not have sent any messages).
    /// Required for FK constraint: chat_admins.telegram_id → telegram_users.telegram_user_id
    /// </summary>
    private static async Task EnsureUserExistsAsync(
        ITelegramUserRepository userRepo,
        global::Telegram.Bot.Types.User telegramUser,
        CancellationToken cancellationToken)
    {
        var existingUser = await userRepo.GetByTelegramIdAsync(telegramUser.Id, cancellationToken);
        if (existingUser != null)
            return;

        // Create minimal user record - they haven't messaged yet so we only have basic profile info
        var now = DateTimeOffset.UtcNow;
        var newUser = new TelegramUser(
            TelegramUserId: telegramUser.Id,
            Username: telegramUser.Username,
            FirstName: telegramUser.FirstName,
            LastName: telegramUser.LastName,
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: telegramUser.IsBot,
            IsTrusted: false, // Will be set to true by auto-trust logic after this
            IsBanned: false,
            BotDmEnabled: false,
            FirstSeenAt: now,
            LastSeenAt: now,
            CreatedAt: now,
            UpdatedAt: now
        );

        await userRepo.UpsertAsync(newUser, cancellationToken);
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
            using var scope = serviceProvider.CreateScope();
            var chatHandler = scope.ServiceProvider.GetRequiredService<IBotChatHandler>();
            var linkedChannelsRepository = scope.ServiceProvider.GetRequiredService<ILinkedChannelsRepository>();
            var photoService = scope.ServiceProvider.GetRequiredService<TelegramPhotoService>();
            var photoHashService = scope.ServiceProvider.GetRequiredService<IPhotoHashService>();

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
