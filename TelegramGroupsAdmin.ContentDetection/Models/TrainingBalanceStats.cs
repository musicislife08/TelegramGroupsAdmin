namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Balance statistics for ML training data (used for UI display).
/// Calculated using same logic as actual training to show accurate balance status.
/// </summary>
public class TrainingBalanceStats
{
    public int ExplicitSpamCount { get; set; }
    public int ImplicitSpamCount { get; set; }
    public int ExplicitHamCount { get; set; }
    public int ImplicitHamCount { get; set; }

    public int SpamCount => ExplicitSpamCount + ImplicitSpamCount;
    public int TotalHamCount => ExplicitHamCount + ImplicitHamCount;
    public int TotalSampleCount => SpamCount + TotalHamCount;

    public double SpamRatio => TotalSampleCount > 0
        ? (double)SpamCount / TotalSampleCount
        : 0.0;
}
