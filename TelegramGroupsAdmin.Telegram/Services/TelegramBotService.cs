using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Telegram bot capabilities - events, state, and config change notifications.
/// NOT a BackgroundService - just the capabilities other services need.
/// The polling lifecycle is handled separately by TelegramBotPollingHost.
/// </summary>
public class TelegramBotService(
    IMessageProcessingService messageProcessingService,
    IChatHealthCache chatHealthCache,
    ILogger<TelegramBotService> logger) : ITelegramBotService
{
    private User? _botUserInfo;

    // Events forwarded from child services (using event accessor pattern)
    public event Action<MessageRecord>? OnNewMessage
    {
        add => messageProcessingService.OnNewMessage += value;
        remove => messageProcessingService.OnNewMessage -= value;
    }

    public event Action<MessageEditRecord>? OnMessageEdited
    {
        add => messageProcessingService.OnMessageEdited += value;
        remove => messageProcessingService.OnMessageEdited -= value;
    }

    public event Action<long, MediaType>? OnMediaUpdated
    {
        add => messageProcessingService.OnMediaUpdated += value;
        remove => messageProcessingService.OnMediaUpdated -= value;
    }

    public event Action<ChatHealthStatus>? OnHealthUpdate
    {
        add => chatHealthCache.OnHealthUpdate += value;
        remove => chatHealthCache.OnHealthUpdate -= value;
    }

    /// <summary>
    /// Event for polling host to subscribe to config changes.
    /// </summary>
    public event Action? ConfigChangeRequested;

    /// <inheritdoc />
    public User? BotUserInfo => _botUserInfo;

    /// <inheritdoc />
    public void NotifyConfigChange()
    {
        logger.LogInformation("Bot configuration change requested, polling host will restart");
        ConfigChangeRequested?.Invoke();
    }

    /// <inheritdoc />
    public void SetBotUserInfo(User? user)
    {
        _botUserInfo = user;
        if (user is not null)
        {
            logger.LogInformation(
                "Bot identity set: {BotDisplayName}",
                user.ToLogInfo());
        }
        else
        {
            logger.LogDebug("Bot identity cleared");
        }
    }
}
