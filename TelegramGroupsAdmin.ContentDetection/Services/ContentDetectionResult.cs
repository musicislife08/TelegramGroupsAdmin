using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Aggregated spam detection result from multiple checks.
/// Uses V2 additive scoring (0.0-5.0+) directly — no V1 conversion.
/// </summary>
public record ContentDetectionResult
{
    /// <summary>
    /// Overall spam determination
    /// </summary>
    public bool IsSpam { get; init; }

    /// <summary>
    /// Total additive score from all checks (0.0-5.0+, SpamAssassin-style)
    /// </summary>
    public double TotalScore { get; init; }

    /// <summary>
    /// Results from individual V2 checks
    /// </summary>
    public List<ContentCheckResponseV2> CheckResults { get; init; } = [];

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

    /// <summary>
    /// Raw Vision API analysis text (populated by ImageContentCheckV2 for downstream veto use)
    /// </summary>
    public string? VisionAnalysisText { get; init; }
}
