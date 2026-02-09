using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot.Exceptions;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for fetching and caching Telegram chat and user profile photos
/// </summary>
public class TelegramPhotoService
{
    // Telegram Bot API file download limit (standard api.telegram.org)
    // Profile photos exceeding this limit are extremely rare
    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20MB

    private readonly ILogger<TelegramPhotoService> _logger;
    private readonly IBotMediaService _mediaService;
    private readonly IBotChatService _chatService;
    private readonly MessageHistoryOptions _options;
    private readonly string _chatIconsPath;
    private readonly string _userPhotosPath;

    public TelegramPhotoService(
        ILogger<TelegramPhotoService> logger,
        IBotMediaService mediaService,
        IBotChatService chatService,
        IOptions<MessageHistoryOptions> options)
    {
        _logger = logger;
        _mediaService = mediaService;
        _chatService = chatService;
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
    public async Task<string?> GetChatIconAsync(long chatId, CancellationToken cancellationToken = default)
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
            var chat = await _chatService.GetChatAsync(chatId, cancellationToken);
            var chatName = chat.Title ?? chat.Username;

            if (chat.Photo == null)
            {
                _logger.LogDebug("Chat {Chat} has no profile photo - skipping icon cache",
                    LogDisplayName.ChatDebug(chatName, chatId));
                return null;
            }

            // Download the small version of the chat photo
            var file = await _mediaService.GetFileAsync(chat.Photo.SmallFileId, cancellationToken);
            if (file.FilePath == null)
            {
                _logger.LogWarning("Unable to get file path for chat {Chat} photo",
                    LogDisplayName.ChatDebug(chatName, chatId));
                return null;
            }

            // Download and resize to square icon
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var fileStream = File.Create(tempPath))
                {
                    await _mediaService.DownloadFileAsync(file.FilePath, fileStream, cancellationToken);
                }

                // Resize to 64x64 icon
                await ResizeImageAsync(tempPath, localPath, 64, cancellationToken);

                _logger.LogDebug("Cached chat icon for {Chat}",
                    LogDisplayName.ChatDebug(chatName, chatId));
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
            // NOTE: This catch block should rarely execute - profile photos are typically small
            // Only catches edge cases where chat has extremely large profile photo (>20MB)
            _logger.LogWarning(
                "Chat icon download failed: File exceeds Telegram Bot API 20MB limit for chat {Chat}. " +
                "Profile photos this large are extremely rare.",
                LogDisplayName.ChatDebug(null, chatId));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch chat icon for {Chat}",
                LogDisplayName.ChatDebug(null, chatId));
            return null;
        }
    }

    /// <summary>
    /// Get or fetch user profile photo
    /// Returns relative path from wwwroot/images or null if not available
    /// </summary>
    /// <param name="userId">Telegram user ID</param>
    /// <param name="user">Optional user record for logging context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<string?> GetUserPhotoAsync(
        long userId,
        TelegramUser? user = null,
        CancellationToken cancellationToken = default)
    {
        var result = await GetUserPhotoWithMetadataAsync(userId, null, user, cancellationToken);
        return result?.RelativePath;
    }

    /// <summary>
    /// Get or fetch user profile photo with smart cache invalidation
    /// Checks file_unique_id to detect photo changes
    /// Returns photo metadata including file_unique_id for storage
    /// </summary>
    /// <param name="userId">Telegram user ID</param>
    /// <param name="knownPhotoId">Known file_unique_id for cache invalidation</param>
    /// <param name="user">Optional user record for logging context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<UserPhotoResult?> GetUserPhotoWithMetadataAsync(
        long userId,
        string? knownPhotoId,
        TelegramUser? user = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = $"{userId}.jpg";
            var localPath = Path.Combine(_userPhotosPath, fileName);
            var relativePath = $"user_photos/{fileName}";

            // Fetch current photo from Telegram
            var photos = await _mediaService.GetUserProfilePhotosAsync(userId, limit: 1, ct: cancellationToken);
            if (photos.TotalCount == 0 || photos.Photos.Length == 0)
            {
                _logger.LogDebug("User {User} has no profile photo", user.ToLogDebug(userId));
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
                    _logger.LogDebug("User {User} photo cached (file_unique_id: {PhotoId})",
                        user.ToLogDebug(userId), currentPhotoId);
                    return new UserPhotoResult(relativePath, currentPhotoId);
                }
                // Photo changed - will re-download below
                _logger.LogDebug("User {User} photo changed ({Old} → {New}), re-downloading",
                    user.ToLogDebug(userId), knownPhotoId, currentPhotoId);
            }

            // Photo changed or first download - fetch from Telegram
            var file = await _mediaService.GetFileAsync(smallestPhoto.FileId, cancellationToken);
            if (file.FilePath == null)
            {
                _logger.LogWarning("Unable to get file path for user {User} photo", user.ToLogDebug(userId));
                return null;
            }

            // Download and resize to square icon
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var fileStream = File.Create(tempPath))
                {
                    await _mediaService.DownloadFileAsync(file.FilePath, fileStream, cancellationToken);
                }

                // Resize to 64x64 icon
                await ResizeImageAsync(tempPath, localPath, 64, cancellationToken);

                _logger.LogDebug("Cached user photo for {User}: {Path}", user.ToLogDebug(userId), relativePath);
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
            // NOTE: This catch block should rarely execute - profile photos are typically small
            // Only catches edge cases where user has extremely large profile photo (>20MB)
            _logger.LogWarning(
                "User photo download failed: File exceeds Telegram Bot API 20MB limit for user {User}. " +
                "Profile photos this large are extremely rare.",
                user.ToLogDebug(userId));
            return null;
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("user not found", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("User photo fetch skipped: user not found for {User}", user.ToLogDebug(userId));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch user photo for {User}", user.ToLogDebug(userId));
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
