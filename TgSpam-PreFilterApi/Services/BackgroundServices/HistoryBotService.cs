using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgSpam_PreFilterApi.Configuration;
using TgSpam_PreFilterApi.Data;
using TgSpam_PreFilterApi.Services.Telegram;

namespace TgSpam_PreFilterApi.Services.BackgroundServices;

public partial class HistoryBotService : BackgroundService
{
    private readonly TelegramBotClientFactory _botFactory;
    private readonly MessageHistoryRepository _repository;
    private readonly TelegramOptions _options;
    private readonly MessageHistoryOptions _historyOptions;
    private readonly ILogger<HistoryBotService> _logger;

    public HistoryBotService(
        TelegramBotClientFactory botFactory,
        MessageHistoryRepository repository,
        IOptions<TelegramOptions> options,
        IOptions<MessageHistoryOptions> historyOptions,
        ILogger<HistoryBotService> logger)
    {
        _botFactory = botFactory;
        _repository = repository;
        _options = options.Value;
        _historyOptions = historyOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botClient = _botFactory.GetOrCreate(_options.HistoryBotToken);

        _logger.LogInformation("HistoryBot started listening for messages in all chats");

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
            _logger.LogInformation("HistoryBot stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HistoryBot encountered fatal error");
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
                urls != null ? JsonSerializer.Serialize(urls) : null
            );

            await _repository.InsertMessageAsync(messageRecord);

            _logger.LogDebug(
                "Cached message {MessageId} from user {UserId} in chat {ChatId} (photo: {HasPhoto}, text: {HasText})",
                message.MessageId,
                message.From.Id,
                message.Chat.Id,
                photoFileId != null,
                text != null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
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
        _logger.LogError(exception, "HistoryBot polling error");
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
