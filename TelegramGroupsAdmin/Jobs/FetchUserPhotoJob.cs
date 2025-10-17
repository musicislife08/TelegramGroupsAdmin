using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;
using Telegram.Bot;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// TickerQ job to fetch and cache user profile photos in telegram_users table
/// Runs asynchronously after message save with 0s delay for instant execution
/// Provides persistence, retry logic, and proper error handling (replaces fire-and-forget)
/// Phase 4.10: Computes and stores pHash (8 bytes) for fast impersonation detection lookups
/// </summary>
public class FetchUserPhotoJob(
    ILogger<FetchUserPhotoJob> logger,
    TelegramBotClientFactory botClientFactory,
    TelegramPhotoService photoService,
    TelegramUserRepository telegramUserRepository,
    IPhotoHashService photoHashService,
    IOptions<TelegramOptions> telegramOptions)
{
    private readonly ILogger<FetchUserPhotoJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramPhotoService _photoService = photoService;
    private readonly TelegramUserRepository _telegramUserRepository = telegramUserRepository;
    private readonly IPhotoHashService _photoHashService = photoHashService;
    private readonly TelegramOptions _telegramOptions = telegramOptions.Value;

    /// <summary>
    /// Fetch user profile photo and update telegram_users table
    /// Scheduled via TickerQ with 0s delay for instant execution
    /// </summary>
    [TickerFunction(functionName: "FetchUserPhoto")]
    public async Task ExecuteAsync(TickerFunctionContext<FetchUserPhotoPayload> context, CancellationToken cancellationToken)
    {
        var payload = context.Request;
        if (payload == null)
        {
            _logger.LogError("FetchUserPhotoJob received null payload");
            return;
        }

        _logger.LogDebug(
            "Fetching user photo for user {UserId} (message {MessageId})",
            payload.UserId,
            payload.MessageId);

        // Get bot client from factory
        var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);

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
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to fetch user photo for user {UserId} (message {MessageId})",
                payload.UserId,
                payload.MessageId);
            throw; // Re-throw to let TickerQ handle retry logic and record exception
        }
    }
}
