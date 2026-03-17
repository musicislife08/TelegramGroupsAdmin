using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for downloading and storing media attachments from Telegram.
/// </summary>
public interface ITelegramMediaService
{
    /// <summary>
    /// Download and save media file from Telegram.
    /// Returns relative filename (e.g., "animation_12345.mp4") for database storage, or null if download failed.
    /// </summary>
    Task<string?> DownloadAndSaveMediaAsync(
        string fileId,
        MediaType mediaType,
        string? fileName,
        long chatId,
        int messageId,
        CancellationToken cancellationToken = default);
}
