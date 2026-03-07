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

}
