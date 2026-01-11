using TelegramGroupsAdmin.ContentDetection.Constants;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// V2 spam check response using additive scoring instead of voting
/// Supports explicit abstention when check finds no evidence
/// </summary>
public record ContentCheckResponseV2
{
    /// <summary>
    /// Name of the check that generated this response
    /// </summary>
    public required CheckName CheckName { get; init; }

    /// <summary>
    /// Spam score contribution (0.0 to 5.0 points)
    /// 0.0 = abstained (no evidence found)
    /// 5.0 = maximum spam signal (e.g., Bayes 99%, CAS banned)
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// Indicates check explicitly abstained (found no evidence)
    /// When true, Score should be 0.0
    /// </summary>
    public required bool Abstained { get; init; }

    /// <summary>
    /// Human-readable explanation of the result
    /// </summary>
    public required string Details { get; init; }

    /// <summary>
    /// Exception that occurred during check execution (if any)
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// Time spent executing this check in milliseconds
    /// </summary>
    public double ProcessingTimeMs { get; init; }

    /// <summary>
    /// OCR-extracted text from image (populated by ImageContentCheckV2 for downstream veto use)
    /// </summary>
    public string? OcrExtractedText { get; init; }

    /// <summary>
    /// Raw text analysis from OpenAI Vision API (reason + patterns).
    /// Stored raw for downstream processing; Details field has formatted version for UI.
    /// </summary>
    public string? VisionAnalysisText { get; init; }
}
