using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic to fetch and cache user profile photos in telegram_users table
/// Runs asynchronously after message save with 0s delay for instant execution
/// Provides persistence, retry logic, and proper error handling (replaces fire-and-forget)
/// Phase 4.10: Computes and stores pHash (8 bytes) for fast impersonation detection lookups
/// </summary>
public class FetchUserPhotoJob(
    ILogger<FetchUserPhotoJob> logger,
    TelegramBotClientFactory botClientFactory,
    TelegramPhotoService photoService,
    ITelegramUserRepository telegramUserRepository,
    IPhotoHashService photoHashService,
    TelegramConfigLoader configLoader) : IJob
{
    private readonly ILogger<FetchUserPhotoJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramPhotoService _photoService = photoService;
    private readonly ITelegramUserRepository _telegramUserRepository = telegramUserRepository;
    private readonly IPhotoHashService _photoHashService = photoHashService;
    private readonly TelegramConfigLoader _configLoader = configLoader;

    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        var payloadJson = context.JobDetail.JobDataMap.GetString("payload")
            ?? throw new InvalidOperationException("payload not found in job data");

        var payload = JsonSerializer.Deserialize<FetchUserPhotoPayload>(payloadJson)
            ?? throw new InvalidOperationException("Failed to deserialize FetchUserPhotoPayload");

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Fetch user profile photo and update telegram_users table
    /// Executed with 0s delay for instant execution
    /// </summary>
    private async Task ExecuteAsync(FetchUserPhotoPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "FetchUserPhoto";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            if (payload == null)
            {
                _logger.LogError("FetchUserPhotoJob received null payload");
                return;
            }

            _logger.LogDebug(
                "Fetching user photo for user {UserId} (message {MessageId})",
                payload.UserId,
                payload.MessageId);

            // Load bot config from database
            var botToken = await _configLoader.LoadConfigAsync();

            // Get bot client from factory
            var botClient = _botClientFactory.GetOrCreate(botToken);

            try
            {
                // Fetch user photo (cached if already downloaded)
                var userPhotoPath = await _photoService.GetUserPhotoAsync(botClient, payload.UserId);

                if (userPhotoPath != null)
                {
                    // Phase 4.10: Compute perceptual hash for fast impersonation detection lookups
                    string? photoHashBase64 = null;
                    try
                    {
                        var photoHashBytes = await _photoHashService.ComputePhotoHashAsync(userPhotoPath);
                        if (photoHashBytes != null)
                        {
                            photoHashBase64 = Convert.ToBase64String(photoHashBytes);
                            _logger.LogDebug(
                                "Computed pHash for user {UserId} (8 bytes → 12 char Base64)",
                                payload.UserId);
                        }
                    }
                    catch (Exception hashEx)
                    {
                        // Log but don't fail job if hash computation fails
                        _logger.LogWarning(
                            hashEx,
                            "Failed to compute photo hash for user {UserId}, continuing without hash",
                            payload.UserId);
                    }

                    // Update telegram_users table with photo path and pHash (centralized storage)
                    await _telegramUserRepository.UpdateUserPhotoPathAsync(
                        payload.UserId,
                        userPhotoPath,
                        photoHashBase64,
                        cancellationToken);

                    _logger.LogInformation(
                        "Cached user photo for user {UserId}: {PhotoPath} (pHash: {HasHash})",
                        payload.UserId,
                        userPhotoPath,
                        photoHashBase64 != null ? "stored" : "none");
                }
                else
                {
                    _logger.LogDebug(
                        "User {UserId} has no profile photo",
                        payload.UserId);
                }

                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to fetch user photo for user {UserId} (message {MessageId})",
                    payload?.UserId,
                    payload?.MessageId);
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
