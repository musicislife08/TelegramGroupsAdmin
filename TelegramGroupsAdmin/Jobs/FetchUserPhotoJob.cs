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
/// </summary>
public class FetchUserPhotoJob(
    ILogger<FetchUserPhotoJob> logger,
    TelegramBotClientFactory botClientFactory,
    TelegramPhotoService photoService,
    TelegramUserRepository telegramUserRepository,
    IOptions<TelegramOptions> telegramOptions)
{
    private readonly ILogger<FetchUserPhotoJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramPhotoService _photoService = photoService;
    private readonly TelegramUserRepository _telegramUserRepository = telegramUserRepository;
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
                // Update telegram_users table with photo path (centralized storage)
                await _telegramUserRepository.UpdateUserPhotoPathAsync(
                    payload.UserId,
                    userPhotoPath,
                    photoHash: null, // TODO: Phase 4.10 - compute pHash for impersonation detection
                    cancellationToken);

                _logger.LogInformation(
                    "Cached user photo for user {UserId}: {PhotoPath}",
                    payload.UserId,
                    userPhotoPath);
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
