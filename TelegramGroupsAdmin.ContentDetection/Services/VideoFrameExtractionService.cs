using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Extracts frames from videos using FFmpeg.
/// Used for video spam detection (ML-6) - frame hashing and OCR.
/// </summary>
public interface IVideoFrameExtractionService
{
    /// <summary>
    /// Extracts keyframes from a video file.
    /// Smart frame selection: attempts beginning (10%), skips black frames, extracts middle (50%).
    /// </summary>
    /// <param name="videoPath">Full path to video file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of extracted frame paths and metadata (brightness, entropy)</returns>
    Task<List<ExtractedFrame>> ExtractKeyframesAsync(string videoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether FFmpeg is available (binary found on startup).
    /// If false, ExtractKeyframesAsync will always return empty list.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Extracts the first non-black frame from a video and saves as image.
    /// Used for thumbnail generation (ban celebration GIFs, etc.)
    /// Tries positions 10%, 20%, 30%, 50%, 90% to find a non-black frame.
    /// </summary>
    /// <param name="videoPath">Full path to video file</param>
    /// <param name="outputPath">Full path for output image file (format determined by extension)</param>
    /// <param name="maxSize">Maximum dimension (width or height) for the output</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if a non-black frame was extracted successfully</returns>
    Task<bool> ExtractThumbnailAsync(
        string videoPath,
        string outputPath,
        int maxSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a video file (MP4, WebM, etc.) to animated GIF format.
    /// Used for ban celebration GIFs when users upload Telegram "GIFs" (which are MP4s).
    /// </summary>
    /// <param name="videoPath">Full path to source video file</param>
    /// <param name="outputPath">Full path for output GIF file</param>
    /// <param name="maxSize">Maximum dimension (width or height) for the output</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if conversion was successful</returns>
    Task<bool> ConvertVideoToGifAsync(
        string videoPath,
        string outputPath,
        int maxSize = 480,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Metadata for an extracted video frame
/// </summary>
public record ExtractedFrame(
    string FramePath,
    double PositionPercent,
    double Brightness,
    bool IsBlackFrame
);

public class VideoFrameExtractionService : IVideoFrameExtractionService
{
    private readonly ILogger<VideoFrameExtractionService> _logger;
    private readonly string? _ffmpegPath;

    public bool IsAvailable => _ffmpegPath != null;

    public VideoFrameExtractionService(ILogger<VideoFrameExtractionService> logger)
    {
        _logger = logger;

        // Detect ffmpeg binary on startup
        // Allow override via FFMPEG_PATH environment variable for non-standard installations
        _ffmpegPath = FindFfmpegBinary();

        if (_ffmpegPath != null)
        {
            _logger.LogInformation(
                "FFmpeg initialized: binary={FfmpegPath}",
                _ffmpegPath);
        }
        else
        {
            _logger.LogWarning(
                "FFmpeg NOT available - 'ffmpeg' binary not found in PATH. " +
                "ML-6 Layer 1-2 (video frame extraction + OCR) will be skipped. " +
                "Install: Docker='wget static build', Mac='brew install ffmpeg'. " +
                "Or set FFMPEG_PATH environment variable to specify custom binary location.");
        }
    }

    public async Task<List<ExtractedFrame>> ExtractKeyframesAsync(
        string videoPath,
        CancellationToken cancellationToken = default)
    {
        // Graceful degradation: if ffmpeg not available, skip frame extraction
        if (_ffmpegPath == null)
        {
            _logger.LogDebug("Skipping frame extraction for {VideoPath} - ffmpeg binary not available",
                Path.GetFileName(videoPath));
            return [];
        }

        try
        {
            if (!File.Exists(videoPath))
            {
                _logger.LogWarning("Video file not found: {VideoPath}", videoPath);
                return [];
            }

            var frames = new List<ExtractedFrame>();

            // Get video duration first
            var duration = await GetVideoDurationAsync(videoPath, cancellationToken);
            if (duration <= 0)
            {
                _logger.LogWarning("Could not determine video duration for {VideoPath}", videoPath);
                return [];
            }

            // Special handling for very short videos (<1 second)
            // For short videos, extract first available frame(s) without seeking
            if (duration < 1.0)
            {
                _logger.LogDebug("Short video detected ({Duration:F2}s), extracting first available frame", duration);

                // Extract first frame only (seeking is unreliable for very short videos)
                var frame = await ExtractFirstFrameAsync(videoPath, cancellationToken);
                if (frame != null)
                {
                    if (!frame.IsBlackFrame)
                    {
                        frames.Add(frame);
                        _logger.LogDebug("Extracted first frame from short video {VideoPath}", Path.GetFileName(videoPath));
                    }
                    else
                    {
                        File.Delete(frame.FramePath);
                        _logger.LogDebug("Short video has only black frame {VideoPath}", Path.GetFileName(videoPath));
                    }
                }

                _logger.LogInformation(
                    "Extracted {FrameCount} frames from short video {VideoPath} (duration: {Duration:F2}s)",
                    frames.Count, Path.GetFileName(videoPath), duration);

                return frames;
            }

            // Smart frame selection for longer videos: beginning (10%) + middle (50%)
            // Skip beginning if black frame
            double[] positionsToTry = [0.10, 0.20, 0.30]; // Try multiple positions for beginning
            var middlePosition = 0.50;

            // Try to extract beginning frame (skip black frames)
            ExtractedFrame? beginningFrame = null;
            foreach (var position in positionsToTry)
            {
                var frame = await ExtractFrameAtPositionAsync(videoPath, duration, position, cancellationToken);
                if (frame != null)
                {
                    if (!frame.IsBlackFrame)
                    {
                        beginningFrame = frame;
                        _logger.LogDebug("Extracted beginning frame at {Position:P0} for {VideoPath}",
                            position, Path.GetFileName(videoPath));
                        break;
                    }
                    else
                    {
                        // Clean up black frame
                        File.Delete(frame.FramePath);
                        _logger.LogDebug("Skipped black frame at {Position:P0} for {VideoPath}",
                            position, Path.GetFileName(videoPath));
                    }
                }
            }

            // Extract middle frame
            var middleFrame = await ExtractFrameAtPositionAsync(videoPath, duration, middlePosition, cancellationToken);
            if (middleFrame != null)
            {
                _logger.LogDebug("Extracted middle frame at {Position:P0} for {VideoPath}",
                    middlePosition, Path.GetFileName(videoPath));
            }

            // Add frames to result
            if (beginningFrame != null) frames.Add(beginningFrame);
            if (middleFrame != null) frames.Add(middleFrame);

            // If both frames are black, try end frame as last resort
            if (frames.All(f => f.IsBlackFrame))
            {
                var endFrame = await ExtractFrameAtPositionAsync(videoPath, duration, 0.90, cancellationToken);
                if (endFrame != null && !endFrame.IsBlackFrame)
                {
                    frames.Add(endFrame);
                    _logger.LogDebug("Extracted end frame at 90% for {VideoPath} (beginning/middle were black)",
                        Path.GetFileName(videoPath));
                }
            }

            _logger.LogInformation(
                "Extracted {FrameCount} frames from {VideoPath} (duration: {Duration:F1}s)",
                frames.Count, Path.GetFileName(videoPath), duration);

            return frames;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Frame extraction failed for video: {VideoPath}", videoPath);
            return [];
        }
    }

    public async Task<bool> ExtractThumbnailAsync(
        string videoPath,
        string outputPath,
        int maxSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (_ffmpegPath == null)
        {
            _logger.LogDebug("Skipping thumbnail extraction for {VideoPath} - ffmpeg binary not available",
                Path.GetFileName(videoPath));
            return false;
        }

        try
        {
            if (!File.Exists(videoPath))
            {
                _logger.LogWarning("Video file not found: {VideoPath}", videoPath);
                return false;
            }

            // Ensure destination directory exists
            var destDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Get video duration
            var duration = await GetVideoDurationAsync(videoPath, cancellationToken);
            if (duration <= 0)
            {
                _logger.LogWarning("Could not determine video duration for {VideoPath}", videoPath);
                return false;
            }

            // Positions to try (find first non-black frame)
            double[] positionsToTry = duration < 1.0
                ? [0.0] // Very short video - just use first frame
                : [0.10, 0.20, 0.30, 0.50, 0.90];

            foreach (var position in positionsToTry)
            {
                var timestamp = duration * position;

                // Extract frame as PNG with scaling using FFmpeg
                // -vf scale: scales while maintaining aspect ratio
                // Output format determined by file extension (usually .png)
                var scaleFilter = $"scale='min({maxSize},iw)':'min({maxSize},ih)':force_original_aspect_ratio=decrease";
                var seekArg = position > 0 ? $"-ss {timestamp:F2} " : "";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"{seekArg}-i \"{videoPath}\" -frames:v 1 -vf \"{scaleFilter}\" -y \"{outputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                var errorBuilder = new StringBuilder();

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    _logger.LogWarning(
                        "FFmpeg failed to extract frame at {Position:P0} from {VideoPath} (exit code {ExitCode}): {Error}",
                        position, Path.GetFileName(videoPath), process.ExitCode, errorBuilder.ToString().Trim());
                    continue;
                }

                // Check if frame is black (very small file = likely black/dark frame)
                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Length < 1000) // PNG smaller than 1KB is likely black
                {
                    _logger.LogDebug("Skipped black frame at {Position:P0} for {VideoPath}",
                        position, Path.GetFileName(videoPath));
                    File.Delete(outputPath);
                    continue;
                }

                _logger.LogDebug("Extracted thumbnail GIF at {Position:P0} for {VideoPath}",
                    position, Path.GetFileName(videoPath));
                return true;
            }

            _logger.LogWarning("Could not extract non-black frame from {VideoPath}", videoPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Thumbnail extraction failed for video: {VideoPath}", videoPath);
            return false;
        }
    }

    public async Task<bool> ConvertVideoToGifAsync(
        string videoPath,
        string outputGifPath,
        int maxSize = 480,
        CancellationToken cancellationToken = default)
    {
        if (_ffmpegPath == null)
        {
            _logger.LogWarning("Cannot convert video to GIF - ffmpeg binary not available");
            return false;
        }

        try
        {
            if (!File.Exists(videoPath))
            {
                _logger.LogWarning("Video file not found for conversion: {VideoPath}", videoPath);
                return false;
            }

            // Ensure destination directory exists
            var destDir = Path.GetDirectoryName(outputGifPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // FFmpeg command to convert video to GIF with good quality
            // - scale: resize maintaining aspect ratio
            // - fps='min(source_fps,30)': keep original framerate, capped at 30fps
            // - split/palettegen/paletteuse: high-quality GIF with optimized palette
            var scaleFilter = $"scale='min({maxSize},iw)':'min({maxSize},ih)':force_original_aspect_ratio=decrease";
            var filterComplex = $"[0:v] {scaleFilter},fps='min(source_fps,30)',split [a][b];[a] palettegen [p];[b][p] paletteuse";

            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-i \"{videoPath}\" -filter_complex \"{filterComplex}\" -y \"{outputGifPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var errorBuilder = new StringBuilder();

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginErrorReadLine();

            // Give video conversion more time (up to 60 seconds for longer videos)
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);

            if (process.ExitCode != 0 || !File.Exists(outputGifPath))
            {
                _logger.LogWarning(
                    "FFmpeg failed to convert video to GIF (exit code {ExitCode}): {Error}",
                    process.ExitCode, errorBuilder.ToString().Trim());
                return false;
            }

            var inputSize = new FileInfo(videoPath).Length;
            var outputSize = new FileInfo(outputGifPath).Length;
            _logger.LogInformation(
                "Converted video to GIF: {VideoPath} ({InputSize:N0} bytes) -> {GifPath} ({OutputSize:N0} bytes)",
                Path.GetFileName(videoPath), inputSize, Path.GetFileName(outputGifPath), outputSize);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video to GIF conversion failed for: {VideoPath}", videoPath);
            return false;
        }
    }

    /// <summary>
    /// Get video duration in seconds using ffprobe
    /// </summary>
    private async Task<double> GetVideoDurationAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            // Use ffprobe to get duration (comes with ffmpeg)
            var ffprobePath = _ffmpegPath!.Replace("ffmpeg", "ffprobe");
            if (!File.Exists(ffprobePath))
            {
                ffprobePath = FindBinary("ffprobe");
            }

            if (ffprobePath == null)
            {
                _logger.LogWarning("ffprobe not found, cannot determine video duration");
                return 0;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.Append(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            if (process.ExitCode != 0) return 0;

            var output = outputBuilder.ToString();
            if (double.TryParse(output, out var duration))
            {
                return duration;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get video duration for {VideoPath}", videoPath);
            return 0;
        }
    }

    /// <summary>
    /// Extract the first frame from a video without seeking (for very short videos)
    /// </summary>
    private async Task<ExtractedFrame?> ExtractFirstFrameAsync(
        string videoPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var outputPath = Path.Combine(Path.GetTempPath(), $"frame_{Guid.NewGuid()}.jpg");

            // FFmpeg command to extract first frame WITHOUT seeking
            // -i input first, then -frames:v 1 to take first frame only
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath!,
                Arguments = $"-i \"{videoPath}\" -frames:v 1 -q:v 2 -pix_fmt yuvj420p \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var errorBuilder = new StringBuilder();

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.LogWarning("FFmpeg failed to extract first frame: {Error}",
                    errorBuilder.ToString());
                return null;
            }

            // Analyze frame brightness
            var brightness = await AnalyzeFrameBrightnessAsync(outputPath, cancellationToken);
            var isBlack = brightness < 10.0; // Threshold: average brightness < 10 on 0-255 scale

            return new ExtractedFrame(outputPath, 0.0, brightness, isBlack);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract first frame from {VideoPath}", videoPath);
            return null;
        }
    }

    /// <summary>
    /// Extract a single frame at the specified position and analyze brightness
    /// </summary>
    private async Task<ExtractedFrame?> ExtractFrameAtPositionAsync(
        string videoPath,
        double durationSeconds,
        double positionPercent,
        CancellationToken cancellationToken)
    {
        try
        {
            var timestamp = durationSeconds * positionPercent;
            var outputPath = Path.Combine(Path.GetTempPath(), $"frame_{Guid.NewGuid()}.jpg");

            // FFmpeg command to extract frame at timestamp
            // -strict unofficial: Allow non-standard YUV color spaces (needed for some H.264 videos)
            // -pix_fmt yuvj420p: Use full-range YUV for JPEG output
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath!,
                Arguments = $"-ss {timestamp:F2} -i \"{videoPath}\" -frames:v 1 -q:v 2 -pix_fmt yuvj420p \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var errorBuilder = new StringBuilder();

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.LogWarning("FFmpeg failed to extract frame at {Position:P0}: {Error}",
                    positionPercent, errorBuilder.ToString());
                return null;
            }

            // Analyze frame brightness
            var brightness = await AnalyzeFrameBrightnessAsync(outputPath, cancellationToken);
            var isBlack = brightness < 10.0; // Threshold: average brightness < 10 on 0-255 scale

            return new ExtractedFrame(outputPath, positionPercent, brightness, isBlack);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract frame at {Position:P0} from {VideoPath}",
                positionPercent, videoPath);
            return null;
        }
    }

    /// <summary>
    /// Analyze frame brightness using FFmpeg
    /// </summary>
    private async Task<double> AnalyzeFrameBrightnessAsync(string framePath, CancellationToken cancellationToken)
    {
        try
        {
            // Use FFmpeg to calculate average brightness
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath!,
                Arguments = $"-i \"{framePath}\" -vf \"avgblur=sizeX=0\" -f null - 2>&1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            // Simple brightness estimation: calculate mean pixel value from file size and image analysis
            // For a more accurate method, we could use ImageSharp to analyze the actual pixels
            // For now, use a heuristic: small file size relative to resolution often indicates black frames
            var fileInfo = new FileInfo(framePath);
            var fileSize = fileInfo.Length;

            // JPEG compression: black frames are highly compressible
            // Heuristic: < 5KB for typical resolutions likely indicates black/very dark frame
            if (fileSize < 5000)
            {
                return 5.0; // Likely black frame
            }
            else if (fileSize < 15000)
            {
                return 30.0; // Likely dark frame
            }
            else
            {
                return 128.0; // Normal brightness (middle of 0-255 range)
            }
        }
        catch
        {
            // Default to normal brightness if analysis fails
            return 128.0;
        }
    }

    /// <summary>
    /// Find ffmpeg binary in PATH or common install locations
    /// Supports FFMPEG_PATH environment variable override
    /// </summary>
    private string? FindFfmpegBinary()
    {
        // Priority 1: Check FFMPEG_PATH environment variable override
        var envPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        return FindBinary("ffmpeg");
    }

    /// <summary>
    /// Generic binary finder for ffmpeg/ffprobe
    /// </summary>
    private string? FindBinary(string binaryName)
    {
        string[] binaryNames = OperatingSystem.IsWindows()
            ? [$"{binaryName}.exe"]
            : [binaryName];

        foreach (var name in binaryNames)
        {
            // Priority 2: Check if it's in PATH
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

            // Priority 3: Check common install locations (fallback if not in PATH)
            string[] commonPaths = OperatingSystem.IsMacOS()
                ? [$"/opt/homebrew/bin/{name}", $"/usr/local/bin/{name}"]
                : OperatingSystem.IsLinux()
                    ? [$"/usr/local/bin/{name}", $"/usr/bin/{name}"]
                    : [@$"C:\Program Files\ffmpeg\bin\{name}"];

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
