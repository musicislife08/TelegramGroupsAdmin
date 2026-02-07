using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Handles bot protection logic including auto-banning unauthorized bots.
/// Scoped service with direct dependency injection.
/// </summary>
public class BotProtectionService(
    IConfigService configService,
    IChatAdminsRepository chatAdminsRepository,
    ITelegramUserRepository telegramUserRepository,
    IBotModerationService moderationService,
    ILogger<BotProtectionService> logger) : IBotProtectionService
{
    public async Task<bool> ShouldAllowBotAsync(Chat chat, User user, ChatMemberUpdated? chatMemberUpdate = null, CancellationToken cancellationToken = default)
    {
        // Not a bot - always allow
        if (!user.IsBot)
        {
            return true;
        }

        // Get effective config for this chat (chat-specific overrides global)
        // Note: IConfigService doesn't support CancellationToken (configuration library)
        var config = await configService.GetEffectiveAsync<BotProtectionConfig>(ConfigType.UrlFilter, chat.Id)
                    ?? BotProtectionConfig.Default;

        // Bot protection disabled - allow all bots
        if (!config.Enabled || !config.AutoBanBots)
        {
            logger.LogDebug("Bot protection disabled for {Chat}, allowing bot {Bot}",
                chat.ToLogDebug(), user.ToLogDebug());
            return true;
        }

        // Check whitelist
        var botUsername = user.Username?.TrimStart('@');
        if (!string.IsNullOrEmpty(botUsername) && config.WhitelistedBots.Any(wb =>
            wb.TrimStart('@').Equals(botUsername, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogInformation("Bot {Bot} is whitelisted in {Chat}",
                user.ToLogInfo(), chat.ToLogInfo());
            return true;
        }

        // Check if bot was invited by an admin
        if (config.AllowAdminInvitedBots && chatMemberUpdate != null)
        {
            var invitedBy = chatMemberUpdate.From;
            if (invitedBy != null)
            {
                // Check if inviter is a chat admin
                var admins = await chatAdminsRepository.GetChatAdminsAsync(chat.Id, cancellationToken);
                var isInviterAdmin = admins.Any(admin => admin.TelegramId == invitedBy.Id);

                if (isInviterAdmin)
                {
                    logger.LogInformation("Bot {Bot} was invited by admin {Admin} in {Chat}",
                        user.ToLogInfo(), invitedBy.ToLogInfo(), chat.ToLogInfo());
                    return true;
                }
                else
                {
                    logger.LogWarning("Bot {Bot} was invited by non-admin {User} in {Chat} - will be banned",
                        user.ToLogDebug(), invitedBy.ToLogDebug(), chat.ToLogDebug());
                }
            }
        }

        // Bot should be banned
        return false;
    }

    public async Task BanBotAsync(Chat chat, User bot, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            // First, upsert bot to telegram_users table to capture username/name before banning
            var now = DateTimeOffset.UtcNow;
            var telegramUser = new TelegramUser(
                TelegramUserId: bot.Id,
                Username: bot.Username,
                FirstName: bot.FirstName,
                LastName: bot.LastName,
                UserPhotoPath: null, // Bots don't need photo fetching
                PhotoHash: null,
                PhotoFileUniqueId: null,
                IsBot: true, // This is a bot
                IsTrusted: false,
                IsBanned: false, // Will be set by moderation after ban
                BotDmEnabled: false, // Bots don't accept DMs
                FirstSeenAt: now,
                LastSeenAt: now,
                CreatedAt: now,
                UpdatedAt: now
            );
            await telegramUserRepository.UpsertAsync(telegramUser, cancellationToken);

            // Ban the bot via moderation service (handles Telegram API + audit trail)
            var result = await moderationService.SyncBanToChatAsync(
                new SyncBanIntent
                {
                    User = UserIdentity.From(bot),
                    Chat = ChatIdentity.From(chat),
                    Executor = Actor.BotProtection,
                    Reason = $"Unauthorized bot: {reason}"
                },
                cancellationToken);

            if (result.Success)
            {
                logger.LogWarning("Banned unauthorized bot {Bot} from {Chat}. Reason: {Reason}",
                    bot.ToLogDebug(), chat.ToLogDebug(), reason);
            }
            else
            {
                logger.LogError("Failed to ban bot {Bot} from {Chat}: {Error}",
                    bot.ToLogDebug(), chat.ToLogDebug(), result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ban bot {Bot} from {Chat}",
                bot.ToLogDebug(), chat.ToLogDebug());
            throw;
        }
    }
}
