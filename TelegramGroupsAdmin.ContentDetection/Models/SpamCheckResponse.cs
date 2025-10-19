namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Result type for content checks (spam, malware, policy violations)
/// Phase 4.13: Expanded to support multiple violation types
/// </summary>
public enum CheckResultType
{
    /// <summary>
    /// Content is clean (no violations)
    /// </summary>
    Clean = 0,

    /// <summary>
    /// Content is spam
    /// </summary>
    Spam = 1,

    /// <summary>
    /// Content needs human review (uncertain classification)
    /// Only used by AI-based checks (OpenAI, future ML checks)
    /// </summary>
    Review = 2,

    /// <summary>
    /// Phase 4.13: Content contains malware (virus, trojan, etc.)
    /// From VirusTotal file scanning
    /// </summary>
    Malware = 3,

    /// <summary>
    /// Phase 4.13: Hard block policy violation (instant ban)
    /// From URL hard blocks, severe policy violations
    /// </summary>
    HardBlock = 4
}

/// <summary>
/// Response from individual content check, based on tg-spam's Response model
/// </summary>
public record ContentCheckResponse
{
    /// <summary>
    /// Name of the check that produced this response
    /// </summary>
    public required string CheckName { get; init; }

    /// <summary>
    /// Classification result (Clean, Spam, or Review)
    /// </summary>
    public CheckResultType Result { get; init; } = CheckResultType.Clean;

    /// <summary>
    /// Human-readable explanation of the decision
    /// </summary>
    public string Details { get; init; } = string.Empty;

    /// <summary>
    /// Confidence score (0-100) for this check's decision
    /// </summary>
    public int Confidence { get; init; } = 0;

    /// <summary>
    /// Error that occurred during check (if any)
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// Additional message IDs to delete (for duplicate detection)
    /// </summary>
    public List<int> ExtraDeleteIds { get; init; } = [];

    /// <summary>
    /// Processing time for this check in milliseconds
    /// </summary>
    public long ProcessingTimeMs { get; init; } = 0;
}

/// <summary>
/// Aggregated result from all content checks
/// </summary>
public record ContentCheckResult
{
    /// <summary>
    /// Individual responses from each check
    /// </summary>
    public required List<ContentCheckResponse> CheckResponses { get; init; }

    /// <summary>
    /// Overall spam decision based on all checks
    /// </summary>
    public bool IsSpam { get; init; } = false;

    /// <summary>
    /// Overall confidence score (0-100)
    /// </summary>
    public int OverallConfidence { get; init; } = 0;

    /// <summary>
    /// Action to take based on confidence thresholds
    /// </summary>
    public SpamAction RecommendedAction { get; init; } = SpamAction.Allow;

    /// <summary>
    /// Summary of the decision for logging/display
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Total processing time for all checks in milliseconds
    /// </summary>
    public long TotalProcessingTimeMs { get; init; } = 0;

    /// <summary>
    /// All extra message IDs to delete (combined from all checks)
    /// </summary>
    public List<int> AllExtraDeleteIds { get; init; } = [];
}

/// <summary>
/// Actions to take based on spam confidence
/// </summary>
public enum SpamAction
{
    /// <summary>
    /// Allow the message (low confidence spam)
    /// </summary>
    Allow = 0,

    /// <summary>
    /// Flag for admin review (medium confidence spam)
    /// </summary>
    ReviewQueue = 1,

    /// <summary>
    /// Auto-ban user and delete message (high confidence spam)
    /// </summary>
    AutoBan = 2
}