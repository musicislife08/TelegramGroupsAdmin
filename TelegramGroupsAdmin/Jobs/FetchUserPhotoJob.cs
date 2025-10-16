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
/// TickerQ job to fetch and cache user profile photos
/// Runs asynchronously after message save with 0s delay for instant execution
/// Provides persistence, retry logic, and proper error handling (replaces fire-and-forget)
/// </summary>
public class FetchUserPhotoJob(
    ILogger<FetchUserPhotoJob> logger,
    TelegramBotClientFactory botClientFactory,
    TelegramPhotoService photoService,
    MessageHistoryRepository messageHistoryRepository,
    IOptions<TelegramOptions> telegramOptions)
{
    private readonly ILogger<FetchUserPhotoJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramPhotoService _photoService = photoService;
    private readonly MessageHistoryRepository _messageHistoryRepository = messageHistoryRepository;
    private readonly TelegramOptions _telegramOptions = telegramOptions.Value;

    /// <summary>
    /// Fetch user profile photo and update message record
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
                // Update message record with photo path
                var message = await _messageHistoryRepository.GetMessageAsync(payload.MessageId);
                if (message != null)
                {
                    var updatedMessage = message with { UserPhotoPath = userPhotoPath };
                    await _messageHistoryRepository.UpdateMessageAsync(updatedMessage);

                    _logger.LogInformation(
                        "Cached user photo for user {UserId} (message {MessageId}): {PhotoPath}",
                        payload.UserId,
                        payload.MessageId,
                        userPhotoPath);
                }
                else
                {
                    _logger.LogWarning(
                        "Message {MessageId} not found when updating user photo for user {UserId}",
                        payload.MessageId,
                        payload.UserId);
                }
            }
            else
            {
                _logger.LogDebug(
                    "User {UserId} has no profile photo (message {MessageId})",
                    payload.UserId,
                    payload.MessageId);
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
