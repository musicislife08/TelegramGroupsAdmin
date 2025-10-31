using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// TickerQ job for periodic chat health monitoring
/// Replaces PeriodicTimer in TelegramAdminBotService (Phase 4: Chat health optimization)
/// Monitors chat permissions, admin lists, invite links
/// </summary>
public class ChatHealthCheckJob
{
    private readonly ChatManagementService _chatService;
    private readonly TelegramBotClientFactory _botFactory;
    private readonly string _botToken;
    private readonly ILogger<ChatHealthCheckJob> _logger;

    public ChatHealthCheckJob(
        ChatManagementService chatService,
        TelegramBotClientFactory botFactory,
        IOptions<TelegramOptions> options,
        ILogger<ChatHealthCheckJob> logger)
    {
        _chatService = chatService;
        _botFactory = botFactory;
        _botToken = options.Value.BotToken;
        _logger = logger;
    }

    [TickerFunction("chat_health_check")]
    public async Task ExecuteAsync(
        TickerFunctionContext<ChatHealthCheckPayload> context,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = context.Request;
            if (payload == null)
            {
                _logger.LogError("ChatHealthCheckJob received null payload");
                return;
            }

            var botClient = _botFactory.GetOrCreate(_botToken);

            if (payload.ChatId.HasValue)
            {
                // Single chat refresh (from manual UI button)
                _logger.LogInformation("Running health check for chat {ChatId}", payload.ChatId.Value);
                await _chatService.RefreshSingleChatAsync(botClient, payload.ChatId.Value, includeIcon: true, cancellationToken);
            }
            else
            {
                // All chats refresh (from recurring job)
                _logger.LogInformation("Running health check for all chats");
                await _chatService.RefreshAllHealthAsync(botClient, cancellationToken);
            }

            _logger.LogInformation("Chat health check completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat health check failed");
            throw; // Re-throw for TickerQ retry logic
        }
    }
}
