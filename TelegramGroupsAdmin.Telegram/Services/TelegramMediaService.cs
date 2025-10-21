using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for downloading and storing media attachments from Telegram
/// Phase 4.X: Handles GIF/Animation, Video, Audio, Voice, Sticker, VideoNote, Document
/// Similar to TelegramPhotoService but for general media files
/// </summary>
public class TelegramMediaService(
    ILogger<TelegramMediaService> logger,
    TelegramBotClientFactory botClientFactory,
    IOptions<TelegramOptions> telegramOptions,
    IOptions<MessageHistoryOptions> historyOptions)
{
    private readonly ILogger<TelegramMediaService> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly string _botToken = telegramOptions.Value.BotToken;
    private readonly string _mediaStoragePath = historyOptions.Value.ImageStoragePath; // Reuse same base path

    /// <summary>
    /// Download and save media file from Telegram
    /// Returns relative path (e.g., "media/animation_12345.mp4") for database storage
    /// </summary>
    public async Task<string?> DownloadAndSaveMediaAsync(
        string fileId,
        MediaType mediaType,
        string? fileName,
        long chatId,
        long messageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var botClient = _botClientFactory.GetOrCreate(_botToken);

            // Get file info from Telegram
            var file = await botClient.GetFile(fileId, cancellationToken);
            if (file.FilePath == null)
            {
                _logger.LogWarning("File path is null for fileId {FileId}", fileId);
                return null;
            }

            // Determine file extension based on media type and file path
            var extension = GetFileExtension(file.FilePath, mediaType, fileName);

            // Create unique filename: {mediaType}_{messageId}_{fileUniqueId}.{ext}
            var uniqueFileName = $"{mediaType.ToString().ToLowerInvariant()}_{messageId}_{file.FileUniqueId}{extension}";

            // Ensure media directory exists
            var mediaDir = Path.Combine(_mediaStoragePath, "media");
            Directory.CreateDirectory(mediaDir);

            // Full local path for storage
            var localFilePath = Path.Combine(mediaDir, uniqueFileName);

            // Download file from Telegram
            await using (var fileStream = System.IO.File.Create(localFilePath))
            {
                await botClient.DownloadFile(file.FilePath, fileStream, cancellationToken);
            }

            _logger.LogInformation(
                "Downloaded {MediaType} file for message {MessageId}: {FileName} ({FileSize} bytes)",
                mediaType,
                messageId,
                uniqueFileName,
                new FileInfo(localFilePath).Length);

            // Return relative path for database (e.g., "media/animation_12345.mp4")
            return $"media/{uniqueFileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to download {MediaType} file {FileId} for message {MessageId}",
                mediaType,
                fileId,
                messageId);
            return null;
        }
    }

    /// <summary>
    /// Determine file extension from Telegram file path, media type, or original filename
    /// </summary>
    private static string GetFileExtension(string telegramFilePath, MediaType mediaType, string? fileName)
    {
        // First try: Extract extension from Telegram file path
        var pathExtension = Path.GetExtension(telegramFilePath);
        if (!string.IsNullOrEmpty(pathExtension))
        {
            return pathExtension;
        }

        // Second try: Extract from original filename (for documents)
        if (!string.IsNullOrEmpty(fileName))
        {
            var fileExtension = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(fileExtension))
            {
                return fileExtension;
            }
        }

        // Fallback: Use media type default extension
        return mediaType switch
        {
            MediaType.Animation => ".mp4",      // Telegram animations are MP4
            MediaType.Video => ".mp4",
            MediaType.Audio => ".mp3",
            MediaType.Voice => ".ogg",          // Telegram voice messages are OGG
            MediaType.Sticker => ".webp",       // Telegram stickers are WebP
            MediaType.VideoNote => ".mp4",      // Circular videos are MP4
            MediaType.Document => ".bin",       // Unknown document type
            _ => ".dat"
        };
    }
}
