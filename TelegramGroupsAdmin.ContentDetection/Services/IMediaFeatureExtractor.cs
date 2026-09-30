using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Computes perceptual-hash media features for a message's photo or video.
/// Every method returns null (never throws for a missing file) when no features can be computed.
/// </summary>
public interface IMediaFeatureExtractor
{
    /// <summary>Hashes the photo at <paramref name="absolutePath"/>; null if the file is missing or unreadable.</summary>
    Task<PhotoFeatures?> ExtractPhotoAsync(string absolutePath);

    /// <summary>Extracts and hashes keyframes of the video at <paramref name="absolutePath"/>; null if the file is missing, FFmpeg is unavailable, or no frame hashes.</summary>
    Task<VideoFeatures?> ExtractVideoAsync(string absolutePath, CancellationToken cancellationToken = default);

    /// <summary>Hashes already-extracted keyframes (the caller owns and cleans up the frame files).</summary>
    Task<VideoFeatures?> FromFramesAsync(IReadOnlyList<ExtractedFrame> frames);
}
