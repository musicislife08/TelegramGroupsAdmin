namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Composite model for Messages page - includes message details and all detection results in one object.
/// PERF-APP-1: Eliminates N+1 query by loading everything in a single JOIN query.
/// </summary>
public class MessageWithDetectionHistory
{
    /// <summary>
    /// The core message data
    /// </summary>
    public required MessageRecord Message { get; init; }

    /// <summary>
    /// All detection results (spam checks) for this message, ordered by DetectedAt DESC.
    /// Empty list if no spam checks have been run on this message.
    /// </summary>
    public List<DetectionResultRecord> DetectionResults { get; init; } = [];

    /// <summary>
    /// Convenience property: Does this message have any spam detection history?
    /// </summary>
    public bool HasDetectionHistory => DetectionResults.Count > 0;

    /// <summary>
    /// Convenience property: Latest detection result (most recent spam check)
    /// </summary>
    public DetectionResultRecord? LatestDetection => DetectionResults.FirstOrDefault();
}
