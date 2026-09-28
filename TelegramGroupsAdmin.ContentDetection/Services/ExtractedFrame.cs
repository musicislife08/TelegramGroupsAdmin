using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Metadata for an extracted video frame
/// </summary>
public record ExtractedFrame(
    string FramePath,
    double PositionPercent,
    double Brightness,
    bool IsBlackFrame
);

public static class ExtractedFrames
{
    /// <summary>
    /// Deletes the frames' temp files. Never throws: a file that is already gone or cannot be deleted
    /// is logged and skipped, so cleanup in a finally block cannot mask the original exception.
    /// </summary>
    public static void DeleteFiles(this IEnumerable<ExtractedFrame> frames, ILogger? logger)
    {
        foreach (var frame in frames)
        {
            try
            {
                if (File.Exists(frame.FramePath))
                    File.Delete(frame.FramePath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to clean up extracted frame at {FramePath}", frame.FramePath);
            }
        }
    }
}
