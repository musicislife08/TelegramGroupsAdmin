namespace TelegramGroupsAdmin.ContentDetection.Constants;

/// <summary>
/// Safety boundary constants for the content detection scoring system.
/// These are architectural invariants (hard limits), not operator-tunable thresholds.
/// For configurable thresholds (AutoBan, ReviewQueue), see ContentDetectionConfig.
/// </summary>
public static class ContentDetectionConstants
{
    /// <summary>
    /// Minimum score any check can return (floor clamp)
    /// </summary>
    public const double MinScore = 0.0;

    /// <summary>
    /// Maximum score any check can return (ceiling clamp)
    /// </summary>
    public const double MaxScore = 5.0;

    /// <summary>
    /// Default AI score when the AI response doesn't include a score value
    /// </summary>
    public const double DefaultAIScore = 2.5;
}
