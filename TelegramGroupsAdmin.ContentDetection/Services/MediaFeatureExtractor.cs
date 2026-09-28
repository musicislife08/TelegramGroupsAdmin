using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <inheritdoc />
public sealed class MediaFeatureExtractor(
    IPhotoHashService photoHashService,
    IVideoFrameExtractionService frameExtractionService,
    ILogger<MediaFeatureExtractor>? logger = null) : IMediaFeatureExtractor
{
    public async Task<PhotoFeatures?> ExtractPhotoAsync(string absolutePath)
    {
        if (!File.Exists(absolutePath))
            return null;
        var hash = await photoHashService.ComputePhotoHashAsync(absolutePath);
        return hash is null ? null : new PhotoFeatures(hash);
    }

    public async Task<VideoFeatures?> ExtractVideoAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(absolutePath) || !frameExtractionService.IsAvailable)
            return null;
        var frames = await frameExtractionService.ExtractKeyframesAsync(absolutePath, cancellationToken);
        try
        {
            return await FromFramesAsync(frames);
        }
        finally
        {
            foreach (var frame in frames)
                File.Delete(frame.FramePath);
        }
    }

    public async Task<VideoFeatures?> FromFramesAsync(IReadOnlyList<ExtractedFrame> frames)
    {
        var keyframes = new List<KeyframeFeature>(frames.Count);
        foreach (var frame in frames)
        {
            var hash = await photoHashService.ComputePhotoHashAsync(frame.FramePath);
            if (hash is not null)
                keyframes.Add(new KeyframeFeature(frame.PositionPercent, hash));
        }

        if (keyframes.Count == 0)
            logger?.LogWarning("No keyframe hashes computed from {Count} frames", frames.Count);
        return keyframes.Count == 0 ? null : new VideoFeatures(keyframes);
    }
}
