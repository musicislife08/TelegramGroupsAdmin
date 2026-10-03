using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for generating thumbnails from images, GIFs, and videos.
/// Uses IImageProcessor for images/GIFs, FFmpeg (via IVideoFrameExtractionService) for videos.
/// </summary>
public class ThumbnailService : IThumbnailService
{
    private readonly IVideoFrameExtractionService _videoFrameService;
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(
        IVideoFrameExtractionService videoFrameService,
        IImageProcessor imageProcessor,
        ILogger<ThumbnailService> logger)
    {
        _videoFrameService = videoFrameService;
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    public async Task<bool> GenerateThumbnailAsync(string sourcePath, string destinationPath, int maxSize = 100, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning("Source file does not exist: {Path}", sourcePath);
                return false;
            }

            // Check if this is a video file by extension or actual content (magic bytes).
            // Giphy and similar services often serve MP4 content from .gif URLs,
            // so the file extension alone is unreliable.
            var extension = Path.GetExtension(sourcePath);
            if (MediaUtilities.VideoExtensions.Contains(extension) || MediaUtilities.IsVideoContent(sourcePath))
            {
                return await GenerateVideoThumbnailAsync(sourcePath, destinationPath, maxSize, ct);
            }

            // Image/GIF processing with IImageProcessor
            return await GenerateImageThumbnailAsync(sourcePath, destinationPath, maxSize, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate thumbnail for {Path}", sourcePath);
            return false;
        }
    }

    /// <summary>
    /// Generate thumbnail from video using FFmpeg (extracts first non-black frame)
    /// </summary>
    private async Task<bool> GenerateVideoThumbnailAsync(string sourcePath, string destinationPath, int maxSize, CancellationToken ct)
    {
        if (!_videoFrameService.IsAvailable)
        {
            _logger.LogWarning("FFmpeg not available, cannot generate thumbnail for video: {Path}", sourcePath);
            return false;
        }

        // FFmpeg extracts a single frame for thumbnail
        var result = await _videoFrameService.ExtractThumbnailAsync(sourcePath, destinationPath, maxSize, ct);

        if (result)
        {
            _logger.LogDebug("Generated video thumbnail: {Source} -> {Dest}", sourcePath, destinationPath);
        }
        else
        {
            _logger.LogWarning("Failed to generate video thumbnail: {Source}", sourcePath);
        }

        return result;
    }

    /// <summary>
    /// Generate a thumbnail from an image or GIF. Animated sources collapse to their
    /// first frame during decode, so the output is always a static PNG.
    /// </summary>
    private async Task<bool> GenerateImageThumbnailAsync(string sourcePath, string destinationPath, int maxSize, CancellationToken ct)
    {
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Decode and resize into a temp file first, then move it into place. This
        // guarantees destinationPath is never truncated or replaced until a new
        // thumbnail has been fully and successfully produced, so a failed
        // regeneration can never destroy a previously-good thumbnail.
        var tempPath = destinationPath + ".tmp";
        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var destination = File.Create(tempPath))
            {
                if (!await _imageProcessor.ResizeToFitAsync(source, destination, maxSize, ImageEncoding.Png, ct))
                {
                    _logger.LogWarning("Could not decode image for thumbnail: {Source}", sourcePath);
                    return false;
                }
            }

            // Only now is the existing thumbnail, if any, replaced.
            File.Move(tempPath, destinationPath, overwrite: true);
            _logger.LogDebug("Generated image thumbnail: {Source} -> {Dest}", sourcePath, destinationPath);
            return true;
        }
        finally
        {
            // Covers the decode-failed return AND any exception unwinding through here.
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException ex)
                {
                    // Best-effort cleanup: a stray .tmp file is harmless and will be
                    // overwritten by the next regeneration attempt at this path.
                    _logger.LogDebug(ex, "Could not delete temp thumbnail file: {TempPath}", tempPath);
                }
            }
        }
    }
}
