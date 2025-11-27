namespace TelegramGroupsAdmin.ContentDetection.Constants;

/// <summary>
/// Shared constants for content detection scoring system (SpamAssassin-inspired)
/// </summary>
public static class ContentDetectionConstants
{
    /// <summary>
    /// Threshold for automatic spam action (≥5.0 points = auto-ban)
    /// </summary>
    public const double SpamThreshold = 5.0;

    /// <summary>
    /// Threshold for review queue (3.0-5.0 points = manual review)
    /// </summary>
    public const double ReviewThreshold = 3.0;

    /// <summary>
    /// Allow threshold (&lt;3.0 points = safe)
    /// </summary>
    public const double AllowThreshold = 3.0;

    /// <summary>
    /// Default confidence value for OpenAI spam detection when API doesn't return confidence
    /// </summary>
    public const double DefaultOpenAIConfidence = 0.8;
}
