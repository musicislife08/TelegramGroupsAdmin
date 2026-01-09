using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Aggregated spam detection result from multiple checks
/// </summary>
public record ContentDetectionResult
{
    /// <summary>
    /// Overall spam determination
    /// </summary>
    public bool IsSpam { get; init; }

    /// <summary>
    /// Highest confidence score from all checks
    /// </summary>
    public int MaxConfidence { get; init; }

    /// <summary>
    /// Average confidence from spam-flagging checks
    /// </summary>
    public int AvgConfidence { get; init; }

    /// <summary>
    /// Number of checks that flagged as spam
    /// </summary>
    public int SpamFlags { get; init; }

    /// <summary>
    /// Phase 2.6: Net confidence from weighted voting
    /// Sum(spam confidences) - Sum(ham confidences)
    /// </summary>
    public int NetConfidence { get; init; }

    /// <summary>
    /// Results from individual checks
    /// </summary>
    public List<ContentCheckResponse> CheckResults { get; init; } = [];

    /// <summary>
    /// Primary reason for spam classification
    /// </summary>
    public string PrimaryReason { get; init; } = string.Empty;

    /// <summary>
    /// Recommended action based on confidence thresholds
    /// </summary>
    public DetectionAction RecommendedAction { get; init; }

    /// <summary>
    /// Whether the message should be submitted to OpenAI for veto
    /// </summary>
    public bool ShouldVeto { get; init; }

    /// <summary>
    /// Phase 4.13: Hard block result if URL pre-filter blocked the message
    /// Non-null indicates instant ban without OpenAI veto
    /// </summary>
    public HardBlockResult? HardBlock { get; init; }

    /// <summary>
    /// OCR-extracted text from image (populated by ImageContentCheckV2 for downstream veto use)
    /// </summary>
    public string? OcrExtractedText { get; init; }
}
