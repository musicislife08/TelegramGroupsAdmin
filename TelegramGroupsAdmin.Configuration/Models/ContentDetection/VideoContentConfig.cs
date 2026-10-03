namespace TelegramGroupsAdmin.Configuration.Models.ContentDetection;

/// <summary>
/// Video content detection configuration (ML-6)
/// </summary>
public class VideoContentConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether video spam detection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to use OpenAI Vision for video frame analysis (ML-6 Layer 3)
    /// </summary>
    public bool UseOpenAIVision { get; set; } = true;

    /// <summary>
    /// Whether to use OCR text extraction from frames before Vision (ML-6 Layer 2)
    /// </summary>
    public bool UseOCR { get; set; } = true;

    /// <summary>
    /// Minimum OCR confidence from text checks to skip OpenAI Vision fallback (0.0-5.0)
    /// If text-based spam checks return score >= this threshold, skip Vision call
    /// Default: 3.75 (confident spam/ham from text checks skips expensive Vision API)
    /// </summary>
    public double OcrConfidenceThreshold { get; set; } = 3.75;

    /// <summary>
    /// Minimum characters extracted by OCR to attempt text-based spam analysis
    /// Default: 10 (too short = not enough context for text checks)
    /// </summary>
    public int MinOcrTextLength { get; set; } = 10;

    /// <summary>
    /// Whether to use keyframe hash similarity matching against training samples (ML-6 Layer 1)
    /// </summary>
    public bool UseHashSimilarity { get; set; } = true;

    /// <summary>
    /// Minimum keyframe hash similarity score (0.0-1.0) to match against spam training samples
    /// Default: 0.85 (85% similar = likely the same spam video with minor modifications)
    /// Higher values = stricter matching (fewer false positives, more false negatives)
    /// </summary>
    public double HashSimilarityThreshold { get; set; } = 0.85;

    /// <summary>
    /// Minimum hash similarity (0.0-1.0) to an admin-verified ham video (current verdict ExplicitHam)
    /// for Layer 1 to abstain and skip OCR/Vision. Any other ham match (an auto-scanned ImplicitHam
    /// anchor, or an ExplicitHam anchor below this threshold) falls through to OCR and Vision.
    /// Spam matching uses <see cref="HashSimilarityThreshold"/> and is unaffected.
    /// Default: 0.95 (configs stored before this setting existed read as the default).
    /// </summary>
    public double HamSkipThreshold { get; set; } = DefaultHamSkipThreshold;

    /// <summary>Default for <see cref="HamSkipThreshold"/>.</summary>
    public const double DefaultHamSkipThreshold = 0.95;

    /// <summary>
    /// Score to assign when keyframe matches a training sample (0.0-5.0)
    /// Default: 4.75 (very confident if we've seen this exact spam video before)
    /// </summary>
    public double HashMatchConfidence { get; set; } = 4.75;

    /// <summary>
    /// Maximum number of training samples to compare against
    /// Limits query size for performance (hash comparison is fast, but DB query has overhead)
    /// Default: 1000 (reasonable for homelab deployment)
    /// </summary>
    public int MaxTrainingSamplesToCompare { get; set; } = 1000;

    /// <summary>
    /// Timeout for video analysis requests
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Always run this check for all users (bypasses trust/admin status)
    /// </summary>
    public bool AlwaysRun { get; set; }
}
