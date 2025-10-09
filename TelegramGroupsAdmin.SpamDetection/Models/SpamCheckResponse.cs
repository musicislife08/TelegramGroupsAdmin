namespace TelegramGroupsAdmin.SpamDetection.Models;

/// <summary>
/// Response from individual spam check, based on tg-spam's Response model
/// </summary>
public record SpamCheckResponse
{
    /// <summary>
    /// Name of the check that produced this response
    /// </summary>
    public required string CheckName { get; init; }

    /// <summary>
    /// Whether the message is considered spam by this check
    /// </summary>
    public bool IsSpam { get; init; } = false;

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
/// Aggregated result from all spam checks
/// </summary>
public record SpamCheckResult
{
    /// <summary>
    /// Individual responses from each check
    /// </summary>
    public required List<SpamCheckResponse> CheckResponses { get; init; }

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