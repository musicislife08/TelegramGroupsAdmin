using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Metrics;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Service layer for Telegram user operations.
/// Wraps IBotUserHandler with GetMe caching (via singleton IBotIdentityCache) and admin checks.
/// </summary>
public class BotUserService(
    IBotUserHandler userHandler,
    IBotIdentityCache identityCache,
    ApiMetrics apiMetrics,
    ILogger<BotUserService> logger) : IBotUserService
{
    public async Task<User> GetMeAsync(CancellationToken ct = default)
    {
        // Return cached if available (singleton cache)
        var cached = identityCache.GetCachedBotUser();
        if (cached != null)
        {
            return cached;
        }

        // Fetch from API and cache
        var botUser = await userHandler.GetMeAsync(ct);
        apiMetrics.RecordTelegramApiCall("get_me", success: true);
        identityCache.SetBotUser(botUser);
        logger.LogDebug("Cached bot user info: @{Username} ({Id})",
            botUser.Username, botUser.Id);

        return botUser;
    }

    public async Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default)
    {
        var member = await userHandler.GetChatMemberAsync(chatId, userId, ct);
        apiMetrics.RecordTelegramApiCall("get_chat_member", success: true);
        return member;
    }

    public async Task<bool> IsAdminAsync(long chatId, long userId, CancellationToken ct = default)
    {
        try
        {
            var member = await userHandler.GetChatMemberAsync(chatId, userId, ct);
            apiMetrics.RecordTelegramApiCall("get_chat_member", success: true);
            return member.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator;
        }
        catch (Exception ex)
        {
            apiMetrics.RecordTelegramApiCall("get_chat_member", success: false);
            logger.LogWarning(ex, "Failed to check admin status for user {UserId} in chat {ChatId}",
                userId, chatId);
            return false;
        }
    }

    public async Task<long> GetBotIdAsync(CancellationToken ct = default)
    {
        // Delegates to GetMeAsync which already records metrics
        var botUser = await GetMeAsync(ct);
        return botUser.Id;
    }
}
