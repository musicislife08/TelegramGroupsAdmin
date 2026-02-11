namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Recent spam detection record for analytics display.
/// Analytics-specific type (operational code uses ContentDetection.Models.DetectionResultRecord)
/// </summary>
public class RecentDetection
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
    /// Who added this detection (Actor system)
    /// </summary>
    public required Actor AddedBy { get; set; }

    public long UserId { get; set; }
    public string? MessageText { get; set; }
    public string? ContentHash { get; set; }
    public int NetConfidence { get; set; }
    public string? CheckResultsJson { get; set; }
    public int EditVersion { get; set; }

    /// <summary>
    /// Translation of the message (if available)
    /// </summary>
    public MessageTranslation? Translation { get; set; }
}
