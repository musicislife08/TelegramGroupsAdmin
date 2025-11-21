using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Handles image detection, download, and thumbnail generation for Telegram photos.
/// Downloads full-size images and generates thumbnails using ImageSharp.
/// </summary>
public class ImageProcessingHandler
{
    private readonly MessageHistoryOptions _historyOptions;
    private readonly ILogger<ImageProcessingHandler> _logger;

    public ImageProcessingHandler(
        IOptions<MessageHistoryOptions> historyOptions,
        ILogger<ImageProcessingHandler> logger)
    {
        _historyOptions = historyOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Process photo attachment from message: detect largest photo, download, and generate thumbnail.
    /// Returns null if no photo found or processing fails.
    /// Fails open on errors (logs warning but doesn't block message storage).
    /// </summary>
    public async Task<ImageProcessingResult?> ProcessImageAsync(
        ITelegramBotClient botClient,
        Message message,
        long chatId,
        int messageId,
        CancellationToken cancellationToken = default)
    {
        // Detect photo in message
        if (message.Photo is not { Length: > 0 } photos)
        {
            return null;
        }

        // Get largest photo version
        var largestPhoto = photos.OrderByDescending(p => p.FileSize).First();
        var photoFileId = largestPhoto.FileId;
        var photoFileSize = largestPhoto.FileSize.HasValue ? (int)largestPhoto.FileSize.Value : (int?)null;

        // Download and process image
        var (fullPath, thumbPath) = await DownloadAndProcessImageAsync(
            botClient,
            photoFileId,
            chatId,
            messageId,
            cancellationToken);

        if (fullPath == null || thumbPath == null)
        {
            return null; // Download/processing failed
        }

        return new ImageProcessingResult(
            FileId: photoFileId,
            FileSize: photoFileSize,
            FullPath: fullPath,
            ThumbnailPath: thumbPath
        );
    }

    /// <summary>
    /// Download photo from Telegram and generate thumbnail using ImageSharp.
    /// Returns relative paths for database storage, or (null, null) on failure.
    /// </summary>
    private async Task<(string? fullPath, string? thumbPath)> DownloadAndProcessImageAsync(
        ITelegramBotClient botClient,
        string photoFileId,
        long chatId,
        long messageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create directory structure: {ImageStoragePath}/media/full/{chat_id}/ and media/thumbs/{chat_id}/
            var basePath = _historyOptions.ImageStoragePath;
            var mediaPath = Path.Combine(basePath, "media");
            var fullDir = Path.Combine(mediaPath, "full", chatId.ToString());
            var thumbDir = Path.Combine(mediaPath, "thumbs", chatId.ToString());

            Directory.CreateDirectory(fullDir);
            Directory.CreateDirectory(thumbDir);

            var fileName = $"{messageId}.jpg";
            var fullPath = Path.Combine(fullDir, fileName);
            var thumbPath = Path.Combine(thumbDir, fileName);

            // Download file from Telegram
            var file = await botClient.GetFile(photoFileId, cancellationToken);
            if (file.FilePath == null)
            {
                _logger.LogWarning("Unable to get file path for photo {FileId}", photoFileId);
                return (null, null);
            }

            // Download to temp file first
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var fileStream = File.Create(tempPath))
                {
                    await botClient.DownloadFile(file.FilePath, fileStream, cancellationToken);
                }

                // Copy to full image location
                File.Copy(tempPath, fullPath, overwrite: true);

                // Generate thumbnail using ImageSharp
                using (var image = await Image.LoadAsync(tempPath, cancellationToken))
                {
                    var thumbnailSize = _historyOptions.ThumbnailSize;
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(thumbnailSize, thumbnailSize),
                        Mode = ResizeMode.Max // Maintain aspect ratio
                    }));

                    await image.SaveAsJpegAsync(thumbPath, cancellationToken);
                }

                _logger.LogDebug(
                    "Downloaded and processed image for message {MessageId} in chat {ChatId}",
                    messageId, chatId);

                // Return relative paths for storage in database
                return ($"full/{chatId}/{fileName}", $"thumbs/{chatId}/{fileName}");
            }
            finally
            {
                // Clean up temp file
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        catch (IOException ioEx)
        {
            // Disk full or permissions error - fail open (don't block message, just skip image)
            _logger.LogWarning(ioEx,
                "Filesystem error downloading image for message {MessageId} in chat {ChatId}. Message will be stored without image.",
                messageId, chatId);
            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error downloading/processing image for message {MessageId} in chat {ChatId}",
                messageId, chatId);
            return (null, null);
        }
    }
}

/// <summary>
/// Result of image processing (detection + download + thumbnail generation)
/// </summary>
public record ImageProcessingResult(
    string FileId,
    int? FileSize,
    string FullPath,       // Relative path: "full/{chatId}/{messageId}.jpg"
    string ThumbnailPath   // Relative path: "thumbs/{chatId}/{messageId}.jpg"
);
