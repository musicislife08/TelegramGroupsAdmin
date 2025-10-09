using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.BotCommands;
using TelegramGroupsAdmin.Services.Telegram;

namespace TelegramGroupsAdmin.Services.BackgroundServices;

public partial class TelegramAdminBotService(
    TelegramBotClientFactory botFactory,
    MessageHistoryRepository repository,
    IOptions<TelegramOptions> options,
    IOptions<MessageHistoryOptions> historyOptions,
    CommandRouter commandRouter,
    ILogger<TelegramAdminBotService> logger)
    : BackgroundService, IMessageHistoryService
{
    private readonly TelegramOptions _options = options.Value;
    private readonly MessageHistoryOptions _historyOptions = historyOptions.Value;

    // Events for real-time UI updates
    public event Action<MessageRecord>? OnNewMessage;
    public event Action<MessageEditRecord>? OnMessageEdited;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check if Telegram admin bot is enabled
        if (!_historyOptions.Enabled)
        {
            logger.LogInformation("Telegram admin bot is disabled (MESSAGEHISTORY__ENABLED=false). Service will not start.");
            return;
        }

        var botClient = botFactory.GetOrCreate(_options.BotToken);

        // Register bot commands in Telegram UI
        await RegisterBotCommandsAsync(botClient, stoppingToken);

        logger.LogInformation("Telegram admin bot started listening for messages in all chats");

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
        // Handle new messages
        if (update.Message is { } message)
        {
            await HandleNewMessageAsync(botClient, message);
            return;
        }

        // Handle edited messages
        if (update.EditedMessage is { } editedMessage)
        {
            await HandleEditedMessageAsync(editedMessage);
            return;
        }
    }

    private async Task HandleNewMessageAsync(ITelegramBotClient botClient, Message message)
    {
        // Process messages from all chats where bot is added
        try
        {
            // Check if message is a bot command
            if (commandRouter.IsCommand(message))
            {
                var response = await commandRouter.RouteCommandAsync(botClient, message);
                if (response != null)
                {
                    await botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: response,
                        parseMode: ParseMode.Markdown,
                        replyParameters: new ReplyParameters { MessageId = message.MessageId });
                }
                return; // Don't save command messages to history
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Extract URLs from message text
            var text = message.Text ?? message.Caption;
            var urls = text != null ? ExtractUrls(text) : null;

            // Get photo file ID if present and download image
            string? photoFileId = null;
            int? photoFileSize = null;
            string? photoLocalPath = null;
            string? photoThumbnailPath = null;

            if (message.Photo is { Length: > 0 } photos)
            {
                var largestPhoto = photos.OrderByDescending(p => p.FileSize).First();
                photoFileId = largestPhoto.FileId;
                photoFileSize = largestPhoto.FileSize.HasValue ? (int)largestPhoto.FileSize.Value : null;

                // Download and process image
                (photoLocalPath, photoThumbnailPath) = await DownloadAndProcessImageAsync(
                    botClient,
                    photoFileId,
                    message.Chat.Id,
                    message.MessageId);
            }

            // Calculate content hash for spam correlation
            var urlsJson = urls != null ? JsonSerializer.Serialize(urls) : "";
            var contentHash = ComputeContentHash(text ?? "", urlsJson);

            var messageRecord = new MessageRecord(
                message.MessageId,
                message.From!.Id,
                message.From.Username ?? message.From.FirstName,
                message.Chat.Id,
                now,
                text,
                photoFileId,
                photoFileSize,
                urlsJson != "" ? urlsJson : null,
                EditDate: message.EditDate.HasValue ? new DateTimeOffset(message.EditDate.Value, TimeSpan.Zero).ToUnixTimeSeconds() : null,
                ContentHash: contentHash,
                ChatName: message.Chat.Title ?? message.Chat.Username,
                PhotoLocalPath: photoLocalPath,
                PhotoThumbnailPath: photoThumbnailPath
            );

            await repository.InsertMessageAsync(messageRecord);

            // Raise event for real-time UI updates
            OnNewMessage?.Invoke(messageRecord);

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

    private async Task<(string? fullPath, string? thumbPath)> DownloadAndProcessImageAsync(
        ITelegramBotClient botClient,
        string photoFileId,
        long chatId,
        long messageId)
    {
        try
        {
            // Create directory structure: {ImageStoragePath}/full/{chat_id}/ and thumbs/{chat_id}/
            var basePath = _historyOptions.ImageStoragePath;
            var fullDir = Path.Combine(basePath, "full", chatId.ToString());
            var thumbDir = Path.Combine(basePath, "thumbs", chatId.ToString());

            Directory.CreateDirectory(fullDir);
            Directory.CreateDirectory(thumbDir);

            var fileName = $"{messageId}.jpg";
            var fullPath = Path.Combine(fullDir, fileName);
            var thumbPath = Path.Combine(thumbDir, fileName);

            // Download file from Telegram
            var file = await botClient.GetFile(photoFileId);
            if (file.FilePath == null)
            {
                logger.LogWarning("Unable to get file path for photo {FileId}", photoFileId);
                return (null, null);
            }

            // Download to temp file first
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var fileStream = System.IO.File.Create(tempPath))
                {
                    await botClient.DownloadFile(file.FilePath, fileStream);
                }

                // Copy to full image location
                System.IO.File.Copy(tempPath, fullPath, overwrite: true);

                // Generate thumbnail using ImageSharp
                using (var image = await Image.LoadAsync(tempPath))
                {
                    var thumbnailSize = _historyOptions.ThumbnailSize;
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(thumbnailSize, thumbnailSize),
                        Mode = ResizeMode.Max // Maintain aspect ratio
                    }));

                    await image.SaveAsJpegAsync(thumbPath);
                }

                logger.LogDebug(
                    "Downloaded and processed image for message {MessageId} in chat {ChatId}",
                    messageId, chatId);

                // Return relative paths for storage in database
                return ($"full/{chatId}/{fileName}", $"thumbs/{chatId}/{fileName}");
            }
            finally
            {
                // Clean up temp file
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }
        catch (IOException ioEx)
        {
            // Disk full or permissions error - fail open (don't block message, just skip image)
            logger.LogWarning(ioEx,
                "Filesystem error downloading image for message {MessageId} in chat {ChatId}. Message will be stored without image.",
                messageId, chatId);
            return (null, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error downloading/processing image for message {MessageId} in chat {ChatId}",
                messageId, chatId);
            return (null, null);
        }
    }

    private async Task HandleEditedMessageAsync(Message editedMessage)
    {
        try
        {
            // Get the old message from the database
            var oldMessage = await repository.GetMessageAsync(editedMessage.MessageId);
            if (oldMessage == null)
            {
                logger.LogWarning(
                    "Received edit for unknown message {MessageId}",
                    editedMessage.MessageId);
                return;
            }

            var oldText = oldMessage.MessageText;
            var newText = editedMessage.Text ?? editedMessage.Caption;

            // Skip if text hasn't actually changed
            if (oldText == newText)
            {
                logger.LogDebug(
                    "Edit event for message {MessageId} but text unchanged, skipping",
                    editedMessage.MessageId);
                return;
            }

            var editDate = editedMessage.EditDate.HasValue
                ? new DateTimeOffset(editedMessage.EditDate.Value, TimeSpan.Zero).ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Extract URLs and calculate content hashes
            var oldUrls = oldText != null ? ExtractUrls(oldText) : null;
            var newUrls = newText != null ? ExtractUrls(newText) : null;

            var oldContentHash = ComputeContentHash(oldText ?? "", oldUrls != null ? JsonSerializer.Serialize(oldUrls) : "");
            var newContentHash = ComputeContentHash(newText ?? "", newUrls != null ? JsonSerializer.Serialize(newUrls) : "");

            // Create edit record
            var editRecord = new MessageEditRecord(
                Id: 0, // Will be set by INSERT
                MessageId: editedMessage.MessageId,
                EditDate: editDate,
                OldText: oldText,
                NewText: newText,
                OldContentHash: oldContentHash,
                NewContentHash: newContentHash
            );

            await repository.InsertMessageEditAsync(editRecord);

            // Update the message in the messages table with new text and edit date
            var updatedMessage = oldMessage with
            {
                MessageText = newText,
                EditDate = editDate,
                Urls = newUrls != null ? JsonSerializer.Serialize(newUrls) : null,
                ContentHash = newContentHash
            };
            await repository.UpdateMessageAsync(updatedMessage);

            // Raise event for real-time UI updates
            OnMessageEdited?.Invoke(editRecord);

            logger.LogInformation(
                "Recorded edit for message {MessageId} in chat {ChatId}",
                editedMessage.MessageId,
                editedMessage.Chat.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error handling edit for message {MessageId}",
                editedMessage.MessageId);
        }
    }

    private static string ComputeContentHash(string messageText, string urls)
    {
        var normalized = $"{messageText.ToLowerInvariant().Trim()}{urls.ToLowerInvariant().Trim()}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }

    private async Task RegisterBotCommandsAsync(ITelegramBotClient botClient, CancellationToken cancellationToken)
    {
        try
        {
            // Register commands with different scopes based on permission levels

            // Default scope - commands for all users (ReadOnly level 0)
            var defaultCommands = commandRouter.GetAvailableCommands(permissionLevel: 0)
                .Select(cmd => new BotCommand
                {
                    Command = cmd.Name,
                    Description = cmd.Description
                })
                .ToArray();

            await botClient.SetMyCommands(
                defaultCommands,
                scope: new BotCommandScopeDefault(),
                cancellationToken: cancellationToken);

            // Admin scope - commands for group admins (Admin level 1+)
            var adminCommands = commandRouter.GetAvailableCommands(permissionLevel: 1)
                .Select(cmd => new BotCommand
                {
                    Command = cmd.Name,
                    Description = cmd.Description
                })
                .ToArray();

            await botClient.SetMyCommands(
                adminCommands,
                scope: new BotCommandScopeAllChatAdministrators(),
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Registered bot commands - {DefaultCount} default, {AdminCount} admin",
                defaultCommands.Length,
                adminCommands.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register bot commands with Telegram");
        }
    }

    private static string GetPermissionName(int level) => level switch
    {
        0 => "ReadOnly",
        1 => "Admin",
        2 => "Owner",
        _ => "Unknown"
    };
}
