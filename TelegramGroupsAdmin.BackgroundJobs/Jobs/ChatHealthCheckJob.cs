using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic for periodic chat health monitoring
/// Replaces PeriodicTimer in TelegramAdminBotService (Phase 4: Chat health optimization)
/// Monitors chat permissions, admin lists, invite links
/// </summary>
[DisallowConcurrentExecution]
public class ChatHealthCheckJob : IJob
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

    /// <summary>
    /// Execute chat health check (Quartz.NET entry point)
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        // Scheduled triggers don't have payloads, manual triggers do
        ChatHealthCheckPayload payload;

        if (context.JobDetail.JobDataMap.ContainsKey(JobDataKeys.PayloadJson))
        {
            // Manual trigger - deserialize provided payload
            var payloadJson = context.JobDetail.JobDataMap.GetString(JobDataKeys.PayloadJson)!;
            payload = JsonSerializer.Deserialize<ChatHealthCheckPayload>(payloadJson)
                ?? throw new InvalidOperationException("Failed to deserialize ChatHealthCheckPayload");
        }
        else
        {
            // Scheduled trigger - use default payload (check all chats)
            payload = new ChatHealthCheckPayload { ChatId = null };
        }

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Execute chat health check (business logic)
    /// </summary>
    private async Task ExecuteAsync(
        ChatHealthCheckPayload payload,
        CancellationToken cancellationToken)
    {
        const string jobName = "ChatHealthCheck";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
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

            try
            {
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
                throw; // Re-throw for retry logic and exception recording
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
