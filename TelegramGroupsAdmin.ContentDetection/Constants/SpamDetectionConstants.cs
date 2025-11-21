namespace TelegramGroupsAdmin.ContentDetection.Constants;

/// <summary>
/// Shared constants for spam detection scoring system (SpamAssassin-inspired)
/// </summary>
public static class SpamDetectionConstants
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
}
