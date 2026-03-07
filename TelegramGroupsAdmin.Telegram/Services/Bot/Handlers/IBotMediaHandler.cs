using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram media/file operations.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for media operations.
/// Services should use IBotMediaService which orchestrates this handler.
/// </summary>
public interface IBotMediaHandler
{
    /// <summary>Get a user's profile photos.</summary>
    Task<UserProfilePhotos> GetUserProfilePhotosAsync(long userId, int offset = 0, int limit = 1, CancellationToken ct = default);

    /// <summary>Get basic info about a file and prepare it for downloading.</summary>
    Task<TGFile> GetFileAsync(string fileId, CancellationToken ct = default);

    /// <summary>Download a file from Telegram servers.</summary>
    Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct = default);
}
