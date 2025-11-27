using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Detection result record for UI display (Phase 4.19: Now uses Actor for attribution)
/// </summary>
public class DetectionResultRecord
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public string DetectionSource { get; set; } = string.Empty;
    public string DetectionMethod { get; set; } = string.Empty;
    public bool IsSpam { get; set; }
    public int Confidence { get; set; }
    public string? Reason { get; set; }

    /// <summary>
    /// Who added this detection (Phase 4.19: Actor system)
    /// </summary>
    public required Actor AddedBy { get; set; }

    public long UserId { get; set; }
    public string? MessageText { get; set; }
    public string? ContentHash { get; set; }
    public bool UsedForTraining { get; set; } = true;
    public int NetConfidence { get; set; }  // Required: computed column is_spam derives from this
    public string? CheckResultsJson { get; set; }  // Phase 2.6: JSON string with all check results
    public int EditVersion { get; set; }            // Phase 2.6: Message version (0 = original, 1+ = edits)

    /// <summary>
    /// Translation of the message (Phase 4.20+: Translation display support)
    /// </summary>
    public MessageTranslation? Translation { get; set; }
}
