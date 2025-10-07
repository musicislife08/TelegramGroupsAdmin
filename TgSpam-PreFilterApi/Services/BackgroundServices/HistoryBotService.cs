using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgSpam_PreFilterApi.Configuration;
using TgSpam_PreFilterApi.Data.Models;
using TgSpam_PreFilterApi.Data.Repositories;
using TgSpam_PreFilterApi.Services.Telegram;

namespace TgSpam_PreFilterApi.Services.BackgroundServices;

public partial class HistoryBotService(
    TelegramBotClientFactory botFactory,
    MessageHistoryRepository repository,
    IOptions<TelegramOptions> options,
    IOptions<MessageHistoryOptions> historyOptions,
    ILogger<HistoryBotService> logger)
    : BackgroundService
{
    private readonly TelegramOptions _options = options.Value;
    private readonly MessageHistoryOptions _historyOptions = historyOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botClient = botFactory.GetOrCreate(_options.HistoryBotToken);

        logger.LogInformation("HistoryBot started listening for messages in all chats");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message],
            DropPendingUpdates = true
        };

        try
        {
            await botClient.ReceiveAsync(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandleErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("HistoryBot stopped");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HistoryBot encountered fatal error");
        }
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
            return;

        // Process messages from all chats where bot is added
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiresAt = now + (_historyOptions.RetentionHours * 3600);

            // Extract URLs from message text
            var text = message.Text ?? message.Caption;
            var urls = text != null ? ExtractUrls(text) : null;

            // Get photo file ID if present
            string? photoFileId = null;
            int? photoFileSize = null;

            if (message.Photo is { Length: > 0 } photos)
            {
                var largestPhoto = photos.OrderByDescending(p => p.FileSize).First();
                photoFileId = largestPhoto.FileId;
                photoFileSize = largestPhoto.FileSize > 0 ? (int)largestPhoto.FileSize : null;
            }

            var messageRecord = new MessageRecord(
                message.MessageId,
                message.From!.Id,
                message.From.Username ?? message.From.FirstName,
                message.Chat.Id,
                now,
                expiresAt,
                text,
                photoFileId,
                photoFileSize,
                urls != null ? JsonSerializer.Serialize(urls) : null,
                EditDate: message.EditDate.HasValue ? new DateTimeOffset(message.EditDate.Value, TimeSpan.Zero).ToUnixTimeSeconds() : null,
                ContentHash: null, // Will be calculated when needed
                ChatName: message.Chat.Title ?? message.Chat.Username,
                PhotoLocalPath: null, // Will be set when image is downloaded
                PhotoThumbnailPath: null // Will be set when thumbnail is generated
            );

            await repository.InsertMessageAsync(messageRecord);

            logger.LogDebug(
                "Cached message {MessageId} from user {UserId} in chat {ChatId} (photo: {HasPhoto}, text: {HasText})",
                message.MessageId,
                message.From.Id,
                message.Chat.Id,
                photoFileId != null,
                text != null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error caching message {MessageId} from user {UserId} in chat {ChatId}",
                message.MessageId,
                message.From?.Id,
                message.Chat?.Id);
        }
    }

    private Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "HistoryBot polling error");
        return Task.CompletedTask;
    }

    private static List<string>? ExtractUrls(string text)
    {
        var matches = UrlRegex().Matches(text);
        return matches.Count > 0
            ? matches.Select(m => m.Value).ToList()
            : null;
    }

    [GeneratedRegex(@"https?://[^\s\]\)\>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();
}
