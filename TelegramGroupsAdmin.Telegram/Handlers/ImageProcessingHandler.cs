using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Handles image detection, download, and thumbnail generation for Telegram photos.
/// Downloads full-size images and generates thumbnails via IImageProcessor.
/// </summary>
public class ImageProcessingHandler
{
    private const int ThumbnailSize = 200;

    private readonly IBotMediaService _mediaService;
    private readonly string _dataPath;
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<ImageProcessingHandler> _logger;

    public ImageProcessingHandler(
        IBotMediaService mediaService,
        IOptions<AppOptions> appOptions,
        IImageProcessor imageProcessor,
        ILogger<ImageProcessingHandler> logger)
    {
        _mediaService = mediaService;
        _dataPath = appOptions.Value.DataPath;
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Process photo attachment from message: detect largest photo, download, and generate thumbnail.
    /// Returns null if no photo found or processing fails.
    /// Fails open on errors (logs warning but doesn't block message storage).
    /// </summary>
    public async Task<ImageProcessingResult?> ProcessImageAsync(
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
        var imagePaths = await DownloadAndProcessImageAsync(
            photoFileId,
            chatId,
            messageId,
            cancellationToken);

        if (imagePaths.FullPath == null || imagePaths.ThumbPath == null)
        {
            return null; // Download/processing failed
        }

        return new ImageProcessingResult(
            FileId: photoFileId,
            FileSize: photoFileSize,
            FullPath: imagePaths.FullPath,
            ThumbnailPath: imagePaths.ThumbPath
        );
    }

    /// <summary>
    /// Download photo from Telegram and generate thumbnail via IImageProcessor.
    /// Returns relative paths for database storage, or (null, null) on failure.
    /// </summary>
    private async Task<ImagePaths> DownloadAndProcessImageAsync(
        string photoFileId,
        long chatId,
        int messageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create directory structure: {DataPath}/media/full/{chat_id}/ and media/thumbs/{chat_id}/
            var basePath = _dataPath;
            var mediaPath = Path.Combine(basePath, "media");
            var fullDir = Path.Combine(mediaPath, "full", chatId.ToString());
            var thumbDir = Path.Combine(mediaPath, "thumbs", chatId.ToString());

            Directory.CreateDirectory(fullDir);
            Directory.CreateDirectory(thumbDir);

            var fileName = $"{messageId}.jpg";
            var fullPath = Path.Combine(fullDir, fileName);
            var thumbPath = Path.Combine(thumbDir, fileName);

            // Download file from Telegram
            var file = await _mediaService.GetFileAsync(photoFileId, cancellationToken);
            if (file.FilePath == null)
            {
                _logger.LogWarning("Unable to get file path for photo {FileId}", photoFileId);
                return new ImagePaths(null, null);
            }

            // Download to temp file first
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var fileStream = File.Create(tempPath))
                {
                    await _mediaService.DownloadFileAsync(file.FilePath, fileStream, cancellationToken);
                }

                // Copy to full image location
                File.Copy(tempPath, fullPath, overwrite: true);

                // Generate thumbnail. Quality 75 matches ImageSharp's JpegEncoder
                // default, which this call site previously relied on — raising it
                // would change every newly stored thumbnail.
                bool thumbSucceeded;
                await using (var thumbSource = File.OpenRead(tempPath))
                await using (var thumbTarget = File.Create(thumbPath))
                {
                    thumbSucceeded = await _imageProcessor.ResizeToFitAsync(
                        thumbSource, thumbTarget, ThumbnailSize, ImageEncoding.Jpeg(75), cancellationToken);
                }

                if (!thumbSucceeded)
                {
                    // ResizeToFitAsync leaves an empty file behind on decode failure
                    // (unlike the old ImageSharp path, which threw before creating one).
                    File.Delete(thumbPath);
                    _logger.LogWarning(
                        "Could not decode image for thumbnail: message {MessageId} in chat {ChatId}",
                        messageId, chatId);
                    return new ImagePaths(null, null);
                }

                _logger.LogDebug(
                    "Downloaded and processed image for message {MessageId} in chat {ChatId}",
                    messageId, chatId);

                // Return relative paths for storage in database
                return new ImagePaths($"full/{chatId}/{fileName}", $"thumbs/{chatId}/{fileName}");
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
            return new ImagePaths(null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error downloading/processing image for message {MessageId} in chat {ChatId}",
                messageId, chatId);
            return new ImagePaths(null, null);
        }
    }
}
