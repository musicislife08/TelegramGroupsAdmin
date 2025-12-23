namespace TelegramGroupsAdmin.BackgroundJobs.Constants;

/// <summary>
/// Constants for file scanning (malware detection).
/// </summary>
public static class FileScanConstants
{
    /// <summary>
    /// Minimum score threshold to classify a file as infected (0-5 scale).
    /// Score >= 4.0 = Infected, < 4.0 = Clean.
    /// </summary>
    public const double InfectedScoreThreshold = 4.0;

    /// <summary>
    /// Multiplier to convert V2 score (0-5.0) to V1 confidence percentage (0-100).
    /// </summary>
    public const int ScoreToConfidenceMultiplier = 20;
}
