using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Composite model for Messages page - includes message details and all detection results in one object.
/// PERF-APP-1: Eliminates N+1 query by loading everything in a single JOIN query.
/// Phase 4.12: Added user tags and notes for contextual display
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
    /// Active tags for the message author (Phase 4.12)
    /// Empty list if user has no tags
    /// </summary>
    public List<UserTag> UserTags { get; init; } = [];

    /// <summary>
    /// Admin notes about the message author (Phase 4.12)
    /// Empty list if user has no notes
    /// </summary>
    public List<AdminNote> UserNotes { get; init; } = [];

    /// <summary>
    /// Convenience property: Does this message have any spam detection history?
    /// </summary>
    public bool HasDetectionHistory => DetectionResults.Count > 0;

    /// <summary>
    /// Convenience property: Latest detection result (most recent spam check)
    /// </summary>
    public DetectionResultRecord? LatestDetection => DetectionResults.FirstOrDefault();

    /// <summary>
    /// Convenience property: Does the message author have tags?
    /// </summary>
    public bool HasTags => UserTags.Count > 0;

    /// <summary>
    /// Convenience property: Does the message author have notes?
    /// </summary>
    public bool HasNotes => UserNotes.Count > 0;
}
