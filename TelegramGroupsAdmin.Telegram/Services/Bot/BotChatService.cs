using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Service layer for Telegram chat operations.
/// Wraps IBotChatHandler with caching and database integration.
/// Scoped service - dependencies injected directly (no internal scope creation).
/// </summary>
public class BotChatService(
    IBotChatHandler chatHandler,
    IBotChatHealthService chatHealthService,
    IChatCache chatCache,
    IChatHealthCache healthCache,
    IConfigRepository configRepo,
    IManagedChatsRepository managedChatsRepo,
    IChatAdminsRepository chatAdminsRepo,
    ITelegramUserRepository userRepo,
    IUserActionsRepository userActionsRepo,
    INotificationService notificationService,
    ILogger<BotChatService> logger) : IBotChatService
{
    public async Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken ct = default)
    {
        return await chatHandler.GetChatAsync(chatId, ct);
    }

    public async Task<IReadOnlyList<ChatMember>> GetAdministratorsAsync(
        long chatId,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        // For now, delegate directly to handler
        // TODO: Add caching layer when admin cache is migrated from ChatManagementService
        var admins = await chatHandler.GetChatAdministratorsAsync(chatId, ct);
        return admins;
    }

    public async Task<string?> GetInviteLinkAsync(long chatId, CancellationToken ct = default)
    {
        try
        {
            // Check database cache first (avoid Telegram API call if cached)
            var cachedConfig = await configRepo.GetByChatIdAsync(chatId, ct);
            if (cachedConfig?.InviteLink != null)
            {
                logger.LogDebug("Using cached invite link for chat {ChatId}", chatId);
                return cachedConfig.InviteLink;
            }

            // Not cached - fetch and cache
            return await FetchAndCacheInviteLinkAsync(chatId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
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
            logger.LogDebug("Refreshing invite link from Telegram for chat {ChatId}", chatId);
            return await FetchAndCacheInviteLinkAsync(chatId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to refresh invite link for chat {ChatId}. Bot may lack admin permissions.",
                chatId);
            return null;
        }
    }

    public async Task LeaveChatAsync(long chatId, CancellationToken ct = default)
    {
        await chatHandler.LeaveChatAsync(chatId, ct);
    }

    public async Task<bool> CheckHealthAsync(long chatId, CancellationToken ct = default)
    {
        try
        {
            // Basic health check - can we get chat info?
            var chat = await chatHandler.GetChatAsync(chatId, ct);
            return chat != null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for chat {ChatId}", chatId);
            return false;
        }
    }

    public IReadOnlyList<long> GetHealthyChatIds()
    {
        return healthCache.GetHealthyChatIds().ToList();
    }

    /// <summary>
    /// Handle MyChatMember updates (bot added/removed, admin promotion/demotion)
    /// Only tracks groups/supergroups - private chats are not managed
    /// </summary>
    public async Task HandleBotMembershipUpdateAsync(ChatMemberUpdated myChatMember, CancellationToken ct = default)
    {
        try
        {
            var chat = myChatMember.Chat;

            // Skip private chats - only manage groups/supergroups
            if (chat.Type == ChatType.Private)
            {
                logger.LogDebug("Skipping MyChatMember update for private chat {Chat}", LogDisplayName.ChatDebug(null, chat.Id));
                return;
            }

            var oldStatus = myChatMember.OldChatMember.Status;
            var newStatus = myChatMember.NewChatMember.Status;
            var isAdmin = newStatus == ChatMemberStatus.Administrator;
            var isActive = isAdmin; // IsActive = has admin permissions only (not just membership)
            var isDeleted = newStatus is ChatMemberStatus.Left or ChatMemberStatus.Kicked;

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
                IsDeleted: isDeleted,
                LastSeenAt: DateTimeOffset.UtcNow,
                SettingsJson: null,
                ChatIconPath: null
            );

            await managedChatsRepo.UpsertAsync(chatRecord, ct);

            // If this is about ANOTHER user (not the bot), update admin cache
            var affectedUser = myChatMember.NewChatMember.User;

            if (affectedUser.IsBot == false) // Admin change for a real user
            {
                var wasAdmin = oldStatus is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;
                var isNowAdmin = newStatus is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;

                if (wasAdmin != isNowAdmin)
                {
                    if (isNowAdmin)
                    {
                        // User promoted to admin - ensure user exists in telegram_users first (FK constraint)
                        await EnsureUserExistsAsync(affectedUser, ct);

                        var isCreator = newStatus == ChatMemberStatus.Creator;
                        await chatAdminsRepo.UpsertAsync(chat.Id, affectedUser.Id, isCreator, cancellationToken: ct);
                        logger.LogInformation(
                            "✅ {User} promoted to {Role} in {Chat}",
                            LogDisplayName.UserInfo(affectedUser.FirstName, affectedUser.LastName, affectedUser.Username, affectedUser.Id),
                            isCreator ? "creator" : "admin",
                            LogDisplayName.ChatInfo(chat.Title, chat.Id));
                    }
                    else
                    {
                        // User demoted from admin
                        await chatAdminsRepo.DeactivateAsync(chat.Id, affectedUser.Id, ct);
                        logger.LogInformation(
                            "❌ {User} demoted from admin in {Chat}",
                            LogDisplayName.UserInfo(affectedUser.FirstName, affectedUser.LastName, affectedUser.Username, affectedUser.Id),
                            LogDisplayName.ChatInfo(chat.Title, chat.Id));
                    }
                }
            }

            // If bot was just promoted to admin, refresh admin cache for this chat immediately
            var botWasAdmin = oldStatus == ChatMemberStatus.Administrator;
            var botIsNowAdmin = newStatus == ChatMemberStatus.Administrator;
            var chatName = chat.Title ?? chat.Username;

            if (!botWasAdmin && botIsNowAdmin)
            {
                logger.LogInformation(
                    "🎉 Bot promoted to admin in {Chat}, refreshing admin cache immediately",
                    LogDisplayName.ChatInfo(chatName, chat.Id));

                // Refresh admin cache immediately instead of waiting for periodic health check (30min)
                await chatHealthService.RefreshChatAdminsAsync(chat.Id, ct);
            }

            if (isActive)
            {
                // Cache SDK Chat when bot joins/is promoted
                chatCache.UpdateChat(chat);

                logger.LogInformation(
                    "✅ Bot added to {ChatType} {Chat} as {Status}",
                    chat.Type,
                    chat.ToLogInfo(),
                    newStatus);
            }
            else if (isDeleted)
            {
                // Remove from cache when bot is kicked/left
                chatCache.RemoveChat(chat.Id);

                logger.LogWarning(
                    "❌ Bot removed from {Chat} - status: {Status}",
                    chat.ToLogDebug(),
                    newStatus);
            }
            else
            {
                // Bot demoted but still in chat - keep cache updated
                chatCache.UpdateChat(chat);

                logger.LogWarning(
                    "⚠️ Bot demoted in {Chat} - status: {Status}",
                    chat.ToLogDebug(),
                    newStatus);
            }

            // Trigger immediate health check when bot status changes
            await chatHealthService.RefreshHealthForChatAsync(chat.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error handling MyChatMember update for {Chat}",
                LogDisplayName.ChatDebug(myChatMember.Chat.Title, myChatMember.Chat.Id));
        }
    }

    /// <summary>
    /// Handle ChatMember updates for admin promotion/demotion (instant permission updates)
    /// Called when any user (not just bot) is promoted/demoted in a managed chat
    /// </summary>
    public async Task HandleAdminStatusChangeAsync(ChatMemberUpdated chatMemberUpdate, CancellationToken ct = default)
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

            if (isNowAdmin)
            {
                // User promoted to admin - ensure user exists in telegram_users first (FK constraint)
                await EnsureUserExistsAsync(user, ct);

                var isCreator = newStatus == ChatMemberStatus.Creator;
                await chatAdminsRepo.UpsertAsync(chat.Id, user.Id, isCreator, ct);

                logger.LogInformation(
                    "⬆️ INSTANT: {User} promoted to {Role} in {Chat}",
                    LogDisplayName.UserInfo(user.FirstName, user.LastName, user.Username, user.Id),
                    isCreator ? "creator" : "admin",
                    LogDisplayName.ChatInfo(chat.Title, chat.Id));

                // AUTO-TRUST: Trust admins globally to prevent spam detection and cross-chat bans
                try
                {
                    var trustAction = new UserActionRecord(
                        Id: 0,
                        UserId: user.Id,
                        ActionType: UserActionType.Trust,
                        MessageId: null,
                        IssuedBy: Actor.AutoTrust,
                        IssuedAt: DateTimeOffset.UtcNow,
                        ExpiresAt: null,
                        Reason: $"Admin in chat {chat.Id} ({chat.Title ?? "Unknown"})"
                    );

                    await userActionsRepo.InsertAsync(trustAction, ct);
                    await userRepo.UpdateTrustStatusAsync(user.Id, isTrusted: true, ct);

                    logger.LogInformation(
                        "Auto-trusted {User} - admin in {Chat}",
                        LogDisplayName.UserInfo(user.FirstName, user.LastName, user.Username, user.Id),
                        LogDisplayName.ChatInfo(chat.Title, chat.Id));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to auto-trust admin {User} in {Chat}",
                        LogDisplayName.UserDebug(user.FirstName, user.LastName, user.Username, user.Id),
                        LogDisplayName.ChatDebug(chat.Title, chat.Id));
                }

                // Phase 5.2: Notify owners about admin promotion
                var displayName = TelegramDisplayName.FormatMention(user.FirstName, user.LastName, user.Username, user.Id);
                _ = notificationService.SendSystemNotificationAsync(
                    eventType: NotificationEventType.ChatAdminChanged,
                    subject: $"New Admin Promoted: {chat.Title ?? "Unknown Chat"}",
                    message: $"A new {(isCreator ? "creator" : "admin")} has been added to '{chat.Title ?? "Unknown"}'.\n\n" +
                             $"User: {displayName}\n" +
                             $"Telegram ID: {user.Id}\n" +
                             $"Chat ID: {chat.Id}\n\n" +
                             $"This is a security notification to keep you informed of permission changes.",
                    cancellationToken: ct);
            }
            else
            {
                // User demoted from admin
                await chatAdminsRepo.DeactivateAsync(chat.Id, user.Id, ct);

                logger.LogInformation(
                    "⬇️ INSTANT: {User} demoted from admin in {Chat}",
                    LogDisplayName.UserInfo(user.FirstName, user.LastName, user.Username, user.Id),
                    LogDisplayName.ChatInfo(chat.Title, chat.Id));

                // Phase 5.2: Notify owners about admin demotion
                var displayName = TelegramDisplayName.FormatMention(user.FirstName, user.LastName, user.Username, user.Id);
                _ = notificationService.SendSystemNotificationAsync(
                    eventType: NotificationEventType.ChatAdminChanged,
                    subject: $"Admin Demoted: {chat.Title ?? "Unknown Chat"}",
                    message: $"An admin has been removed from '{chat.Title ?? "Unknown"}'.\n\n" +
                             $"User: {displayName}\n" +
                             $"Telegram ID: {user.Id}\n" +
                             $"Chat ID: {chat.Id}\n\n" +
                             $"This is a security notification to keep you informed of permission changes.",
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            var u = chatMemberUpdate.NewChatMember.User;
            var c = chatMemberUpdate.Chat;
            logger.LogError(ex, "Failed to handle admin status change for {User} in {Chat}",
                LogDisplayName.UserDebug(u.FirstName, u.LastName, u.Username, u.Id),
                LogDisplayName.ChatDebug(c.Title, c.Id));
        }
    }

    /// <summary>
    /// Handle Group → Supergroup migration
    /// When a Group is upgraded to Supergroup (e.g., granting admin), Telegram:
    /// 1. Creates a new Supergroup with different chat ID
    /// 2. Old Group becomes inaccessible (invalid chat ID)
    /// We delete the old chat record since the ID is now invalid
    /// </summary>
    public async Task HandleChatMigrationAsync(long oldChatId, long newChatId, CancellationToken ct = default)
    {
        try
        {
            // Delete old chat record (the old chat ID is now invalid)
            var oldChat = await managedChatsRepo.GetByChatIdAsync(oldChatId, ct);
            if (oldChat != null)
            {
                await managedChatsRepo.DeleteAsync(oldChatId, ct);

                logger.LogInformation(
                    "Deleted old Group {OldChatId} record (migrated to Supergroup {NewChatId})",
                    oldChatId,
                    newChatId);
            }

            // The new Supergroup will be added automatically via HandleBotMembershipUpdateAsync
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
    /// Fetch current invite link from Telegram API and cache in database.
    /// Only updates cache if link has changed (reduces unnecessary writes).
    /// </summary>
    private async Task<string?> FetchAndCacheInviteLinkAsync(long chatId, CancellationToken ct)
    {
        var chat = await chatHandler.GetChatAsync(chatId, ct);
        string? currentLink;

        // Public group - use username link (e.g., https://t.me/groupname)
        if (!string.IsNullOrEmpty(chat.Username))
        {
            currentLink = $"https://t.me/{chat.Username}";
            logger.LogDebug("Got public invite link for {Chat}: {Link}",
                LogDisplayName.ChatDebug(chat.Title, chatId), currentLink);

            // Cache public group link too (username could change)
            var cachedConfig = await configRepo.GetByChatIdAsync(chatId, ct);
            if (cachedConfig?.InviteLink != currentLink)
            {
                await configRepo.SaveInviteLinkAsync(chatId, currentLink, ct);
                logger.LogDebug("Cached public invite link for chat {ChatId}", chatId);
            }

            return currentLink;
        }

        // Private group - check if we already have a cached link
        // ExportChatInviteLink GENERATES a new link (revokes old), so we must avoid calling it
        var cachedConfigPrivate = await configRepo.GetByChatIdAsync(chatId, ct);

        if (cachedConfigPrivate?.InviteLink != null)
        {
            // Use cached link - don't call ExportChatInviteLink (it would revoke this one)
            logger.LogDebug("Using existing cached invite link for private chat {ChatId}", chatId);
            return cachedConfigPrivate.InviteLink;
        }

        // No cached link - export the primary link (this WILL revoke any previous primary link)
        // This should only happen on first setup
        currentLink = await chatHandler.ExportChatInviteLinkAsync(chatId, ct);
        logger.LogWarning(
            "Exported PRIMARY invite link for private chat {ChatId} - this revokes previous primary link! Link: {Link}",
            chatId,
            currentLink);

        // Cache it so we never call ExportChatInviteLink again for this chat
        await configRepo.SaveInviteLinkAsync(chatId, currentLink, ct);
        return currentLink;
    }

    /// <summary>
    /// Ensures a Telegram user exists in the database before creating dependent records (e.g., chat_admins).
    /// Creates a minimal user record if the user doesn't exist yet (they may not have sent any messages).
    /// Required for FK constraint: chat_admins.telegram_id → telegram_users.telegram_user_id
    /// </summary>
    private async Task EnsureUserExistsAsync(User telegramUser, CancellationToken ct)
    {
        var existingUser = await userRepo.GetByTelegramIdAsync(telegramUser.Id, ct);
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

        await userRepo.UpsertAsync(newUser, ct);
    }
}
