using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing video training samples (ML-6)
/// </summary>
public class VideoTrainingSamplesRepository : IVideoTrainingSamplesRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IVideoFrameExtractionService _frameExtractionService;
    private readonly IPhotoHashService _photoHashService;
    private readonly ILogger<VideoTrainingSamplesRepository> _logger;

    public VideoTrainingSamplesRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        IVideoFrameExtractionService frameExtractionService,
        IPhotoHashService photoHashService,
        ILogger<VideoTrainingSamplesRepository> logger)
    {
        _contextFactory = contextFactory;
        _frameExtractionService = frameExtractionService;
        _photoHashService = photoHashService;
        _logger = logger;
    }

    /// <summary>
    /// Save a video training sample from a labeled message
    /// Extracts keyframes, computes perceptual hashes, and stores with spam/ham label
    /// </summary>
    public async Task<bool> SaveTrainingSampleAsync(
        long messageId,
        bool isSpam,
        Actor markedBy,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            // Get message to check for video
            var message = await context.Messages
                .AsNoTracking()
                .Where(m => m.MessageId == messageId)
                .Select(m => new
                {
                    m.MessageId,
                    m.MediaType,
                    m.MediaLocalPath,
                    m.MediaDuration,
                    m.MediaFileSize
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (message == null)
            {
                _logger.LogWarning("Cannot save video training sample: Message {MessageId} not found", messageId);
                return false;
            }

            // Check if message has a video with local path
            // Videos can be: Video, Animation (GIF), VideoNote (round video)
            var isVideo = message.MediaType is Data.Models.MediaType.Video
                or Data.Models.MediaType.Animation
                or Data.Models.MediaType.VideoNote;

            if (!isVideo || string.IsNullOrEmpty(message.MediaLocalPath))
            {
                _logger.LogDebug("Message {MessageId} has no video or local path, skipping video training sample", messageId);
                return false;
            }

            if (!File.Exists(message.MediaLocalPath))
            {
                _logger.LogWarning("Video file not found at {VideoPath} for message {MessageId}, cannot save training sample",
                    message.MediaLocalPath, messageId);
                return false;
            }

            var videoPath = message.MediaLocalPath;

            // Check if FFmpeg is available
            if (!_frameExtractionService.IsAvailable)
            {
                _logger.LogWarning("Cannot save video training sample: FFmpeg not available");
                return false;
            }

            // Extract keyframes from video
            var frames = await _frameExtractionService.ExtractKeyframesAsync(videoPath, cancellationToken);
            if (frames.Count == 0)
            {
                _logger.LogWarning("Failed to extract keyframes from video for message {MessageId}, cannot save training sample", messageId);
                return false;
            }

            // Compute perceptual hashes for each keyframe
            var keyframeHashes = new List<KeyframeHash>();
            foreach (var frame in frames)
            {
                try
                {
                    var hash = await _photoHashService.ComputePhotoHashAsync(frame.FramePath);
                    if (hash != null)
                    {
                        keyframeHashes.Add(new KeyframeHash
                        {
                            Position = frame.PositionPercent,
                            Hash = Convert.ToBase64String(hash)
                        });
                    }

                    // Clean up extracted frame
                    File.Delete(frame.FramePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to compute hash for keyframe at {Position:P0}", frame.PositionPercent);
                    // Continue with other frames
                }
            }

            if (keyframeHashes.Count == 0)
            {
                _logger.LogWarning("Failed to compute hashes for any keyframes from message {MessageId}, cannot save training sample", messageId);
                return false;
            }

            // Detect if video has audio track
            var hasAudio = await DetectAudioTrackAsync(videoPath, cancellationToken);

            // Serialize keyframe hashes to JSON
            var keyframeHashesJson = JsonSerializer.Serialize(keyframeHashes);

            // Check if training sample already exists for this message
            var existingSample = await context.VideoTrainingSamples
                .Where(vts => vts.MessageId == messageId)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingSample != null)
            {
                _logger.LogDebug("Video training sample already exists for message {MessageId}, skipping duplicate", messageId);
                return false;
            }

            // Extract video metadata (width/height) from file
            var (width, height) = await GetVideoDimensionsAsync(videoPath, cancellationToken);

            // Create training sample
            var trainingSample = new VideoTrainingSampleDto
            {
                MessageId = messageId,
                VideoPath = videoPath,
                DurationSeconds = message.MediaDuration ?? 0,
                FileSizeBytes = (int)(message.MediaFileSize ?? 0),
                Width = width,
                Height = height,
                KeyframeHashes = keyframeHashesJson,
                HasAudio = hasAudio,
                IsSpam = isSpam,
                MarkedAt = DateTimeOffset.UtcNow,
                // Actor System: Set exactly one actor field
                MarkedByWebUserId = markedBy.Type == ActorType.WebUser ? markedBy.WebUserId : null,
                MarkedByTelegramUserId = markedBy.Type == ActorType.TelegramUser ? markedBy.TelegramUserId : null,
                MarkedBySystemIdentifier = markedBy.Type == ActorType.System ? markedBy.SystemIdentifier : null
            };

            context.VideoTrainingSamples.Add(trainingSample);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Saved video training sample for message {MessageId}: {Label}, {FrameCount} keyframes, hasAudio={HasAudio} (marked by {ActorType})",
                messageId, isSpam ? "SPAM" : "HAM", keyframeHashes.Count, hasAudio, markedBy.Type);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save video training sample for message {MessageId}", messageId);
            return false;
        }
    }

    /// <summary>
    /// Get video dimensions (width x height) using ffprobe
    /// </summary>
    private async Task<(int Width, int Height)> GetVideoDimensionsAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            var ffprobePath = FindFfprobeBinary();
            if (ffprobePath == null)
            {
                _logger.LogDebug("ffprobe not found, cannot determine video dimensions");
                return (0, 0);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            if (process.ExitCode != 0)
            {
                return (0, 0);
            }

            // Parse output: "1920x1080"
            var output = outputBuilder.ToString().Trim();
            var parts = output.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
            {
                return (width, height);
            }

            return (0, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get video dimensions for {VideoPath}", videoPath);
            return (0, 0);
        }
    }

    /// <summary>
    /// Detect if video has audio track using ffprobe
    /// </summary>
    private async Task<bool> DetectAudioTrackAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            // Use ffprobe to check for audio streams
            // This is a simple heuristic - ffprobe will be in same location as ffmpeg
            var ffprobePath = FindFfprobeBinary();
            if (ffprobePath == null)
            {
                _logger.LogDebug("ffprobe not found, assuming video has no audio");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -select_streams a:0 -show_entries stream=codec_type -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            if (process.ExitCode != 0)
            {
                return false; // No audio stream found
            }

            var output = outputBuilder.ToString().Trim();
            return output.Equals("audio", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect audio track for {VideoPath}, assuming no audio", videoPath);
            return false;
        }
    }

    /// <summary>
    /// Find ffprobe binary (companion to ffmpeg)
    /// </summary>
    private string? FindFfprobeBinary()
    {
        // Check FFMPEG_PATH first and derive ffprobe path
        var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            var ffprobePath = ffmpegPath.Replace("ffmpeg", "ffprobe");
            if (File.Exists(ffprobePath))
            {
                return ffprobePath;
            }
        }

        // Search PATH for ffprobe
        var binaryNames = OperatingSystem.IsWindows()
            ? new[] { "ffprobe.exe" }
            : new[] { "ffprobe" };

        foreach (var name in binaryNames)
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var searchPaths = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            foreach (var searchPath in searchPaths)
            {
                var fullPath = Path.Combine(searchPath, name);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            // Check common locations
            var commonPaths = OperatingSystem.IsMacOS()
                ? new[] { $"/opt/homebrew/bin/{name}", $"/usr/local/bin/{name}" }
                : OperatingSystem.IsLinux()
                    ? new[] { $"/usr/local/bin/{name}", $"/usr/bin/{name}" }
                    : new[] { @$"C:\Program Files\ffmpeg\bin\{name}" };

            foreach (var commonPath in commonPaths)
            {
                if (File.Exists(commonPath))
                {
                    return commonPath;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Represents a keyframe hash with its position in the video
/// Used for JSON serialization in video_training_samples.keyframe_hashes
/// </summary>
internal class KeyframeHash
{
    public double Position { get; set; }
    public string Hash { get; set; } = string.Empty;
}
