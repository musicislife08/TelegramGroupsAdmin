using System.Diagnostics;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Services;
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
    private readonly TelegramConfigLoader _configLoader;
    private readonly IConfigService _configService;
    private readonly ILogger<ChatHealthCheckJob> _logger;

    public ChatHealthCheckJob(
        ChatManagementService chatService,
        TelegramBotClientFactory botFactory,
        TelegramConfigLoader configLoader,
        IConfigService configService,
        ILogger<ChatHealthCheckJob> logger)
    {
        _chatService = chatService;
        _botFactory = botFactory;
        _configLoader = configLoader;
        _configService = configService;
        _logger = logger;
    }

    [TickerFunction("chat_health_check")]
    public async Task ExecuteAsync(
        TickerFunctionContext<ChatHealthCheckPayload> context,
        CancellationToken cancellationToken)
    {
        const string jobName = "ChatHealthCheck";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            try
            {
                var payload = context.Request;
                if (payload == null)
                {
                    _logger.LogError("ChatHealthCheckJob received null payload");
                    return;
                }

                // Check if bot is enabled before running health checks
                var botConfig = await _configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, 0)
                               ?? TelegramBotConfig.Default;

                if (!botConfig.BotEnabled)
                {
                    _logger.LogDebug("Skipping chat health check - bot is disabled");
                    success = true; // Not a failure, just skipped
                    return;
                }

                // Load bot config from database
                var botToken = await _configLoader.LoadConfigAsync();
                var botClient = _botFactory.GetOrCreate(botToken);

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
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat health check failed");
                throw; // Re-throw for TickerQ retry logic
            }
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Record metrics (using TagList to avoid boxing/allocations)
            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }
}
