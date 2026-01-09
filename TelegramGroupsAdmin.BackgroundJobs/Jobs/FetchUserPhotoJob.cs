using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic to fetch and cache user profile photos in telegram_users table
/// Runs asynchronously after message save with 0s delay for instant execution
/// Provides persistence, retry logic, and proper error handling (replaces fire-and-forget)
/// Phase 4.10: Computes and stores pHash (8 bytes) for fast impersonation detection lookups
/// </summary>
public class FetchUserPhotoJob(
    ILogger<FetchUserPhotoJob> logger,
    ITelegramBotClientFactory botClientFactory,
    TelegramPhotoService photoService,
    ITelegramUserRepository telegramUserRepository,
    IPhotoHashService photoHashService,
    IConfigService configService,
    IOptions<MessageHistoryOptions> historyOptions) : IJob
{
    private readonly ILogger<FetchUserPhotoJob> _logger = logger;
    private readonly ITelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramPhotoService _photoService = photoService;
    private readonly ITelegramUserRepository _telegramUserRepository = telegramUserRepository;
    private readonly IPhotoHashService _photoHashService = photoHashService;
    private readonly IConfigService _configService = configService;
    private readonly MessageHistoryOptions _historyOptions = historyOptions.Value;

    public async Task Execute(IJobExecutionContext context)
    {
        var payload = await JobPayloadHelper.TryGetPayloadAsync<FetchUserPhotoPayload>(context, _logger);
        if (payload == null) return;

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
            // Check if bot is enabled before making Telegram API calls
            var botConfig = await _configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, 0)
                            ?? TelegramBotConfig.Default;

            if (!botConfig.BotEnabled)
            {
                _logger.LogInformation("Skipping user photo fetch - bot is disabled");
                success = true; // Not a failure, just skipped
                return;
            }

            // Get user for logging context
            var user = await _telegramUserRepository.GetByTelegramIdAsync(payload.UserId, cancellationToken);

            _logger.LogDebug(
                "Fetching user photo for {User} (message {MessageId})",
                user.ToLogDebug(payload.UserId),
                payload.MessageId);

            try
            {
                // Fetch user photo (cached if already downloaded)
                var userPhotoPath = await _photoService.GetUserPhotoAsync(payload.UserId, user);

                if (userPhotoPath != null)
                {
                    // Phase 4.10: Compute perceptual hash for fast impersonation detection lookups
                    string? photoHashBase64 = null;
                    try
                    {
                        // Resolve relative path to absolute for disk operations
                        var absolutePath = MediaPathUtilities.ToAbsolutePath(userPhotoPath, _historyOptions.ImageStoragePath);
                        var photoHashBytes = await _photoHashService.ComputePhotoHashAsync(absolutePath);
                        if (photoHashBytes != null)
                        {
                            photoHashBase64 = Convert.ToBase64String(photoHashBytes);
                            _logger.LogDebug(
                                "Computed pHash for {User} (8 bytes → 12 char Base64)",
                                user.ToLogDebug(payload.UserId));
                        }
                    }
                    catch (Exception hashEx)
                    {
                        // Log but don't fail job if hash computation fails
                        _logger.LogWarning(
                            hashEx,
                            "Failed to compute photo hash for {User}, continuing without hash",
                            user.ToLogDebug(payload.UserId));
                    }

                    // Update telegram_users table with photo path and pHash (centralized storage)
                    await _telegramUserRepository.UpdateUserPhotoPathAsync(
                        payload.UserId,
                        userPhotoPath,
                        photoHashBase64,
                        cancellationToken);

                    _logger.LogInformation(
                        "Cached user photo for {User}: {PhotoPath} (pHash: {HasHash})",
                        user.ToLogInfo(payload.UserId),
                        userPhotoPath,
                        photoHashBase64 != null ? "stored" : "none");
                }
                else
                {
                    _logger.LogDebug(
                        "User {User} has no profile photo",
                        user.ToLogDebug(payload.UserId));
                }

                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to fetch user photo for {User} (message {MessageId})",
                    user.ToLogDebug(payload.UserId),
                    payload.MessageId);
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
