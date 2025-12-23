namespace TelegramGroupsAdmin.ContentDetection.Constants;

/// <summary>
/// Constants for AI-based content detection.
/// </summary>
public static class AIConstants
{
    /// <summary>
    /// AI cache duration in hours - how long to cache AI responses by content hash
    /// </summary>
    public const int CacheDurationHours = 1;

    /// <summary>
    /// Maximum tokens for AI spam detection response
    /// </summary>
    public const int SpamDetectionMaxTokens = 300;

    /// <summary>
    /// Maximum tokens for image vision analysis response
    /// </summary>
    public const int ImageVisionMaxTokens = 300;

    /// <summary>
    /// Maximum tokens for video vision analysis response
    /// </summary>
    public const int VideoVisionMaxTokens = 300;

    /// <summary>
    /// Confidence multiplier for converting 0-1 confidence to 0-5 score
    /// </summary>
    public const double ConfidenceToScoreMultiplier = 5.0;
}
