using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for generating thumbnails from images and GIFs using ImageSharp
/// </summary>
public class ThumbnailService : IThumbnailService
{
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(ILogger<ThumbnailService> logger)
    {
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

            _logger.LogDebug("Generated thumbnail: {Source} -> {Dest}", sourcePath, destinationPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate thumbnail for {Path}", sourcePath);
            return false;
        }
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
