using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for protecting chats against unauthorized bots
/// Phase 6.1: Bot Auto-Ban
/// </summary>
public interface IBotProtectionService
{
    /// <summary>
    /// Check if a user is a bot and should be banned based on configuration
    /// Returns true if the bot should be allowed (whitelisted or admin-invited)
    /// Returns false if the bot should be banned
    /// </summary>
    Task<bool> ShouldAllowBotAsync(long chatId, User user, ChatMemberUpdated? chatMemberUpdate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ban a bot from the chat and log the event
    /// </summary>
    Task BanBotAsync(ITelegramBotClient botClient, long chatId, User bot, string reason, CancellationToken cancellationToken = default);
}

public class BotProtectionService : IBotProtectionService
{
    private readonly ILogger<BotProtectionService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public BotProtectionService(
        ILogger<BotProtectionService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> ShouldAllowBotAsync(long chatId, User user, ChatMemberUpdated? chatMemberUpdate = null, CancellationToken cancellationToken = default)
    {
        // Not a bot - always allow
        if (!user.IsBot)
        {
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Get effective config for this chat (chat-specific overrides global)
        // Note: IConfigService doesn't support CancellationToken (configuration library)
        var config = await configService.GetEffectiveAsync<BotProtectionConfig>(ConfigType.BotProtection, chatId)
                    ?? BotProtectionConfig.Default;

        // Bot protection disabled - allow all bots
        if (!config.Enabled || !config.AutoBanBots)
        {
            _logger.LogDebug("Bot protection disabled for chat {ChatId}, allowing bot {BotUsername} ({BotId})",
                chatId, user.Username ?? "unknown", user.Id);
            return true;
        }

        // Check whitelist
        var botUsername = user.Username?.TrimStart('@');
        if (!string.IsNullOrEmpty(botUsername) && config.WhitelistedBots.Any(wb =>
            wb.TrimStart('@').Equals(botUsername, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Bot {BotUsername} ({BotId}) is whitelisted in chat {ChatId}",
                user.Username, user.Id, chatId);
            return true;
        }

        // Check if bot was invited by an admin
        if (config.AllowAdminInvitedBots && chatMemberUpdate != null)
        {
            var invitedBy = chatMemberUpdate.From;
            if (invitedBy != null)
            {
                // Check if inviter is a chat admin
                var admins = await chatAdminsRepository.GetChatAdminsAsync(chatId, cancellationToken);
                var isInviterAdmin = admins.Any(admin => admin.TelegramId == invitedBy.Id);

                if (isInviterAdmin)
                {
                    _logger.LogInformation("Bot {BotUsername} ({BotId}) was invited by admin {AdminUsername} ({AdminId}) in chat {ChatId}",
                        user.Username ?? "unknown", user.Id, invitedBy.Username ?? "unknown", invitedBy.Id, chatId);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Bot {BotUsername} ({BotId}) was invited by non-admin {Username} ({UserId}) in chat {ChatId} - will be banned",
                        user.Username ?? "unknown", user.Id, invitedBy.Username ?? "unknown", invitedBy.Id, chatId);
                }
            }
        }

        // Bot should be banned
        return false;
    }

    public async Task BanBotAsync(ITelegramBotClient botClient, long chatId, User bot, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            // First, upsert bot to telegram_users table to capture username/name before banning
            var telegramUserRepo = scope.ServiceProvider.GetRequiredService<TelegramUserRepository>();
            var now = DateTimeOffset.UtcNow;
            var telegramUser = new TelegramUser(
                TelegramUserId: bot.Id,
                Username: bot.Username,
                FirstName: bot.FirstName,
                LastName: bot.LastName,
                UserPhotoPath: null, // Bots don't need photo fetching
                PhotoHash: null,
                IsTrusted: false,
                FirstSeenAt: now,
                LastSeenAt: now,
                CreatedAt: now,
                UpdatedAt: now
            );
            await telegramUserRepo.UpsertAsync(telegramUser, cancellationToken);

            // Ban the bot
            await botClient.BanChatMember(chatId, bot.Id, cancellationToken: cancellationToken);

            _logger.LogWarning("Banned unauthorized bot {BotUsername} ({BotId}) from chat {ChatId}. Reason: {Reason}",
                bot.Username ?? "unknown", bot.Id, chatId, reason);

            // Log to user_actions table for audit trail
            var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();

            var action = new UserActionRecord(
                Id: 0,
                UserId: bot.Id,
                ActionType: UserActionType.Ban,
                MessageId: null,
                IssuedBy: Actor.FromSystem("bot_protection"),
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Permanent ban
                Reason: $"Unauthorized bot: {reason}"
            );

            await userActionsRepository.InsertAsync(action, cancellationToken);

            _logger.LogInformation("Logged bot ban to audit trail for bot {BotId} in chat {ChatId}", bot.Id, chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ban bot {BotUsername} ({BotId}) from chat {ChatId}",
                bot.Username ?? "unknown", bot.Id, chatId);
            throw;
        }
    }
}
