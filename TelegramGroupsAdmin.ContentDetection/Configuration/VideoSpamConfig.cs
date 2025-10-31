namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// Video spam detection configuration (ML-6)
/// </summary>
public class VideoSpamConfig
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
    /// Minimum OCR confidence from text checks to skip OpenAI Vision fallback (0-100)
    /// If text-based spam checks return confidence >= this threshold, skip Vision call
    /// Default: 75% (confident spam/ham from text checks skips expensive Vision API)
    /// </summary>
    public int OcrConfidenceThreshold { get; set; } = 75;

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
    /// Confidence level to assign when keyframe matches a training sample (0-100)
    /// Default: 95 (very confident if we've seen this exact spam video before)
    /// </summary>
    public int HashMatchConfidence { get; set; } = 95;

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
}
