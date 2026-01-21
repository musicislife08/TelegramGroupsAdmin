namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for generating thumbnails from images and GIFs
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// Generates a thumbnail from an image or GIF file.
    /// For GIFs, extracts and uses the first frame.
    /// </summary>
    /// <param name="sourcePath">Full path to the source image/GIF</param>
    /// <param name="destinationPath">Full path where thumbnail will be saved</param>
    /// <param name="maxSize">Maximum dimension (width or height) for the thumbnail</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if thumbnail was generated successfully, false otherwise</returns>
    Task<bool> GenerateThumbnailAsync(string sourcePath, string destinationPath, int maxSize = 100, CancellationToken ct = default);
}
