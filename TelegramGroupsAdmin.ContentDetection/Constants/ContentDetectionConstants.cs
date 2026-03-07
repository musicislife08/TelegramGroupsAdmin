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

}
