using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Service layer for Telegram media/file operations.
/// Wraps IBotMediaHandler with photo caching and image processing.
/// </summary>
public class BotMediaService : IBotMediaService
{
    private readonly IBotMediaHandler _mediaHandler;
    private readonly IBotChatHandler _chatHandler;
    private readonly ILogger<BotMediaService> _logger;
    private readonly string _chatIconsPath;
    private readonly string _userPhotosPath;

    public BotMediaService(
        IBotMediaHandler mediaHandler,
        IBotChatHandler chatHandler,
        IOptions<MessageHistoryOptions> options,
        ILogger<BotMediaService> logger)
    {
        _mediaHandler = mediaHandler;
        _chatHandler = chatHandler;
        _logger = logger;

        // Create subdirectories for chat icons and user photos under media/
        var mediaPath = Path.Combine(options.Value.ImageStoragePath, "media");
        _chatIconsPath = Path.Combine(mediaPath, "chat_icons");
        _userPhotosPath = Path.Combine(mediaPath, "user_photos");

        Directory.CreateDirectory(_chatIconsPath);
        Directory.CreateDirectory(_userPhotosPath);
    }

    public async Task<UserPhotoResult?> GetUserPhotoAsync(
        long userId,
        string? knownPhotoId = null,
        TelegramUser? user = null,
        CancellationToken ct = default)
    {
        try
        {
            var fileName = $"{userId}.jpg";
            var localPath = Path.Combine(_userPhotosPath, fileName);
            var relativePath = $"user_photos/{fileName}";

            // Fetch current photo from Telegram
            var photos = await _mediaHandler.GetUserProfilePhotosAsync(userId, limit: 1, ct: ct);
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
            if (System.IO.File.Exists(localPath))
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
            var file = await _mediaHandler.GetFileAsync(smallestPhoto.FileId, ct);
            if (file.FilePath == null)
            {
                _logger.LogWarning("Unable to get file path for user {User} photo", user.ToLogDebug(userId));
                return null;
            }

            // Download and resize to square icon
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var fileStream = System.IO.File.Create(tempPath))
                {
                    await _mediaHandler.DownloadFileAsync(file.FilePath, fileStream, ct);
                }

                // Resize to 64x64 icon
                await ResizeImageAsync(tempPath, localPath, 64, ct);

                _logger.LogDebug("Cached user photo for {User}: {Path}", user.ToLogDebug(userId), relativePath);
                return new UserPhotoResult(relativePath, currentPhotoId);
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("file is too big"))
        {
            _logger.LogWarning(
                "User photo download failed: File exceeds Telegram Bot API 20MB limit for user {User}.",
                user.ToLogDebug(userId));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch user photo for {User}", user.ToLogDebug(userId));
            return null;
        }
    }

    public async Task<string?> GetChatIconAsync(long chatId, CancellationToken ct = default)
    {
        try
        {
            var fileName = $"{Math.Abs(chatId)}.jpg";
            var localPath = Path.Combine(_chatIconsPath, fileName);
            var relativePath = $"chat_icons/{fileName}";

            // Return cached if exists
            if (System.IO.File.Exists(localPath))
            {
                return relativePath;
            }

            // Fetch chat info from Telegram
            var chat = await _chatHandler.GetChatAsync(chatId, ct);
            var chatName = chat.Title ?? chat.Username;

            if (chat.Photo == null)
            {
                _logger.LogDebug("Chat {Chat} has no profile photo - skipping icon cache",
                    LogDisplayName.ChatDebug(chatName, chatId));
                return null;
            }

            // Download the small version of the chat photo
            var file = await _mediaHandler.GetFileAsync(chat.Photo.SmallFileId, ct);
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
                await using (var fileStream = System.IO.File.Create(tempPath))
                {
                    await _mediaHandler.DownloadFileAsync(file.FilePath, fileStream, ct);
                }

                // Resize to 64x64 icon
                await ResizeImageAsync(tempPath, localPath, 64, ct);

                _logger.LogDebug("Cached chat icon for {Chat}",
                    LogDisplayName.ChatDebug(chatName, chatId));
                return relativePath;
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("file is too big"))
        {
            _logger.LogWarning(
                "Chat icon download failed: File exceeds Telegram Bot API 20MB limit for chat {ChatId}.",
                chatId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch chat icon for chat {ChatId}", chatId);
            return null;
        }
    }

    public async Task<TGFile> GetFileAsync(string fileId, CancellationToken ct = default)
    {
        return await _mediaHandler.GetFileAsync(fileId, ct);
    }

    public async Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct = default)
    {
        await _mediaHandler.DownloadFileAsync(filePath, destination, ct);
    }

    public async Task<byte[]> DownloadFileAsBytesAsync(string fileId, CancellationToken ct = default)
    {
        var file = await _mediaHandler.GetFileAsync(fileId, ct);
        if (file.FilePath == null)
        {
            throw new InvalidOperationException($"Unable to get file path for file {fileId}");
        }

        using var memoryStream = new MemoryStream();
        await _mediaHandler.DownloadFileAsync(file.FilePath, memoryStream, ct);
        return memoryStream.ToArray();
    }

    public async Task<UserProfilePhotos> GetUserProfilePhotosAsync(
        long userId,
        int offset = 0,
        int limit = 1,
        CancellationToken ct = default)
    {
        return await _mediaHandler.GetUserProfilePhotosAsync(userId, offset, limit, ct);
    }

    /// <summary>
    /// Resize image to square icon using ImageSharp.
    /// </summary>
    private static async Task ResizeImageAsync(
        string sourcePath,
        string targetPath,
        int size,
        CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync(sourcePath, ct);

        // Crop to center square, then resize
        image.Mutate(x => x
            .Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));

        await image.SaveAsJpegAsync(targetPath, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
        {
            Quality = 85
        }, ct);
    }
}
