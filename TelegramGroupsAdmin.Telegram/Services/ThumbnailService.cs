using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for generating thumbnails from images, GIFs, and videos.
/// Uses ImageSharp for images/GIFs, FFmpeg (via IVideoFrameExtractionService) for videos.
/// </summary>
public class ThumbnailService : IThumbnailService
{
    private readonly IVideoFrameExtractionService _videoFrameService;
    private readonly ILogger<ThumbnailService> _logger;

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".avi", ".mkv", ".m4v"
    };

    public ThumbnailService(
        IVideoFrameExtractionService videoFrameService,
        ILogger<ThumbnailService> logger)
    {
        _videoFrameService = videoFrameService;
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

            // Check if this is a video file
            var extension = Path.GetExtension(sourcePath);
            if (VideoExtensions.Contains(extension))
            {
                return await GenerateVideoThumbnailAsync(sourcePath, destinationPath, maxSize, ct);
            }

            // Image/GIF processing with ImageSharp
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
    /// Generate thumbnail from image/GIF using ImageSharp
    /// </summary>
    private async Task<bool> GenerateImageThumbnailAsync(string sourcePath, string destinationPath, int maxSize, CancellationToken ct)
    {
        // Ensure destination directory exists
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Load image
        using var image = await Image.LoadAsync<Rgba32>(sourcePath, ct);

        // For animated images (GIFs), extract only the first frame
        // This prevents saving as APNG which would still animate
        using var firstFrame = ExtractFirstFrame(image);

        // Resize maintaining aspect ratio
        firstFrame.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(maxSize, maxSize),
            Mode = ResizeMode.Max
        }));

        // Save as PNG (now guaranteed to be static single-frame)
        await firstFrame.SaveAsPngAsync(destinationPath, ct);

        _logger.LogDebug("Generated image thumbnail: {Source} -> {Dest}", sourcePath, destinationPath);
        return true;
    }

    /// <summary>
    /// Extracts the first frame from a potentially multi-frame image (GIF/APNG)
    /// </summary>
    private static Image<Rgba32> ExtractFirstFrame(Image<Rgba32> source)
    {
        // Clone just the first frame into a new single-frame image
        return source.Frames.CloneFrame(0);
    }
}
