namespace TelegramGroupsAdmin.ContentDetection.Models;

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
    public DetectionAction RecommendedAction { get; init; } = DetectionAction.Allow;

    /// <summary>
    /// Summary of the decision for logging/display
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Total processing time for all checks in milliseconds
    /// </summary>
    public double TotalProcessingTimeMs { get; init; } = 0;

    /// <summary>
    /// All extra message IDs to delete (combined from all checks)
    /// </summary>
    public List<int> AllExtraDeleteIds { get; init; } = [];
}
