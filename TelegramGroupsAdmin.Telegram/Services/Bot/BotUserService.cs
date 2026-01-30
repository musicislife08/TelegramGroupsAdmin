using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Service layer for Telegram user operations.
/// Wraps IBotUserHandler with GetMe caching and admin checks.
/// </summary>
public class BotUserService : IBotUserService
{
    private readonly IBotUserHandler _userHandler;
    private readonly ILogger<BotUserService> _logger;

    // Cached bot user info (thread-safe, lazy initialization)
    private User? _cachedBotUser;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public BotUserService(
        IBotUserHandler userHandler,
        ILogger<BotUserService> logger)
    {
        _userHandler = userHandler;
        _logger = logger;
    }

    public async Task<User> GetMeAsync(CancellationToken ct = default)
    {
        // Return cached if available
        if (_cachedBotUser != null)
        {
            return _cachedBotUser;
        }

        // Thread-safe lazy initialization
        await _cacheLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cachedBotUser != null)
            {
                return _cachedBotUser;
            }

            _cachedBotUser = await _userHandler.GetMeAsync(ct);
            _logger.LogDebug("Cached bot user info: @{Username} ({Id})",
                _cachedBotUser.Username, _cachedBotUser.Id);

            return _cachedBotUser;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default)
    {
        return await _userHandler.GetChatMemberAsync(chatId, userId, ct);
    }

    public async Task<bool> IsAdminAsync(long chatId, long userId, CancellationToken ct = default)
    {
        try
        {
            var member = await _userHandler.GetChatMemberAsync(chatId, userId, ct);
            return member.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check admin status for user {UserId} in chat {ChatId}",
                userId, chatId);
            return false;
        }
    }

    public async Task<long> GetBotIdAsync(CancellationToken ct = default)
    {
        var botUser = await GetMeAsync(ct);
        return botUser.Id;
    }
}
