using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using TelegramGroupsAdmin.Configuration;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for fetching and caching Telegram chat and user profile photos
/// </summary>
public class TelegramPhotoService
{
    private readonly ILogger<TelegramPhotoService> _logger;
    private readonly MessageHistoryOptions _options;
    private readonly string _chatIconsPath;
    private readonly string _userPhotosPath;

    public TelegramPhotoService(
        ILogger<TelegramPhotoService> logger,
        IOptions<MessageHistoryOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        // Create subdirectories for chat icons and user photos under media/
        var mediaPath = Path.Combine(_options.ImageStoragePath, "media");
        _chatIconsPath = Path.Combine(mediaPath, "chat_icons");
        _userPhotosPath = Path.Combine(mediaPath, "user_photos");

        Directory.CreateDirectory(_chatIconsPath);
        Directory.CreateDirectory(_userPhotosPath);
    }

    /// <summary>
    /// Get or fetch chat icon (group/channel photo)
    /// Returns relative path from wwwroot/images or null if not available
    /// </summary>
    public async Task<string?> GetChatIconAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = $"{Math.Abs(chatId)}.jpg"; // Use absolute value for filename
            var localPath = Path.Combine(_chatIconsPath, fileName);
            var relativePath = $"chat_icons/{fileName}"; // Web path (served as /media/chat_icons/)

            // Return cached if exists
            if (File.Exists(localPath))
            {
                return relativePath;
            }

            // Fetch from Telegram
            var chat = await botClient.GetChat(chatId, cancellationToken);
            if (chat.Photo == null)
            {
                _logger.LogInformation("Chat {ChatId} ({ChatName}) has no profile photo - skipping icon cache", chatId, chat.Title ?? chat.Username ?? "Unknown");
                return null;
            }

            // Download the small version of the chat photo
            var file = await botClient.GetFile(chat.Photo.SmallFileId, cancellationToken);
            if (file.FilePath == null)
            {
                _logger.LogWarning("Unable to get file path for chat {ChatId} photo", chatId);
                return null;
            }

            // Download and resize to square icon
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var fileStream = File.Create(tempPath))
                {
                    await botClient.DownloadFile(file.FilePath, fileStream, cancellationToken);
                }

                // Resize to 64x64 icon
                await ResizeImageAsync(tempPath, localPath, 64, cancellationToken);

                _logger.LogInformation("Cached chat icon for {ChatId}", chatId);
                return relativePath;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("file is too big"))
        {
            _logger.LogWarning(
                "Skipping chat icon download for {ChatId}: File exceeds Telegram Bot API 20MB limit. " +
                "To download large profile photos, configure self-hosted Bot API server (Settings → Telegram Bot → API Server URL).",
                chatId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch chat icon for {ChatId}", chatId);
            return null;
        }
    }

    /// <summary>
    /// Get or fetch user profile photo
    /// Returns relative path from wwwroot/images or null if not available
    /// </summary>
    public async Task<string?> GetUserPhotoAsync(ITelegramBotClient botClient, long userId, CancellationToken cancellationToken = default)
    {
        var result = await GetUserPhotoWithMetadataAsync(botClient, userId, null, cancellationToken);
        return result?.RelativePath;
    }

    /// <summary>
    /// Get or fetch user profile photo with smart cache invalidation
    /// Checks file_unique_id to detect photo changes
    /// Returns photo metadata including file_unique_id for storage
    /// </summary>
    public async Task<UserPhotoResult?> GetUserPhotoWithMetadataAsync(
        ITelegramBotClient botClient,
        long userId,
        string? knownPhotoId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = $"{userId}.jpg";
            var localPath = Path.Combine(_userPhotosPath, fileName);
            var relativePath = $"user_photos/{fileName}";

            // Fetch current photo from Telegram
            var photos = await botClient.GetUserProfilePhotos(userId, limit: 1, cancellationToken: cancellationToken);
            if (photos.TotalCount == 0 || photos.Photos.Length == 0)
            {
                _logger.LogDebug("User {UserId} has no profile photo", userId);
                return null;
            }

            // Get the smallest size of the first photo
            var photo = photos.Photos[0];
            var smallestPhoto = photo.OrderBy(p => p.FileSize).First();
            var currentPhotoId = smallestPhoto.FileUniqueId;

            // Smart cache check: return cached if file exists and photo hasn't changed
            // If knownPhotoId is null (first fetch), just check file existence to avoid race conditions
            if (File.Exists(localPath))
            {
                if (knownPhotoId == null || knownPhotoId == currentPhotoId)
                {
                    _logger.LogDebug("User {UserId} photo cached (file_unique_id: {PhotoId})", userId, currentPhotoId);
                    return new UserPhotoResult(relativePath, currentPhotoId);
                }
                // Photo changed - will re-download below
                _logger.LogDebug("User {UserId} photo changed ({Old} → {New}), re-downloading", userId, knownPhotoId, currentPhotoId);
            }

            // Photo changed or first download - fetch from Telegram
            var file = await botClient.GetFile(smallestPhoto.FileId, cancellationToken);
            if (file.FilePath == null)
            {
                _logger.LogWarning("Unable to get file path for user {UserId} photo", userId);
                return null;
            }

            // Download and resize to square icon
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var fileStream = File.Create(tempPath))
                {
                    await botClient.DownloadFile(file.FilePath, fileStream, cancellationToken);
                }

                // Resize to 64x64 icon
                await ResizeImageAsync(tempPath, localPath, 64, cancellationToken);

                _logger.LogInformation("Cached user photo for {UserId} (file_unique_id: {PhotoId})", userId, currentPhotoId);
                return new UserPhotoResult(relativePath, currentPhotoId);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("file is too big"))
        {
            _logger.LogWarning(
                "Skipping user photo download for {UserId}: File exceeds Telegram Bot API 20MB limit. " +
                "To download large profile photos, configure self-hosted Bot API server (Settings → Telegram Bot → API Server URL).",
                userId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch user photo for {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Resize image to square icon using ImageSharp
    /// </summary>
    private async Task ResizeImageAsync(string sourcePath, string targetPath, int size, CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync(sourcePath, cancellationToken);

        // Crop to center square, then resize
        image.Mutate(x => x
            .Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Crop, // Crop to fill square
                Position = AnchorPositionMode.Center
            }));

        await image.SaveAsJpegAsync(targetPath, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
        {
            Quality = 85
        }, cancellationToken);
    }
}
