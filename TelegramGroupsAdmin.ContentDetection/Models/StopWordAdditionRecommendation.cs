namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Recommendation to ADD a new stop word based on spam corpus analysis
/// </summary>
public record StopWordAdditionRecommendation
{
    /// <summary>
    /// The word recommended to be added to stop words list
    /// </summary>
    public required string Word { get; init; }

    /// <summary>
    /// Frequency of this word in spam training samples (percentage, 0-100)
    /// </summary>
    public required decimal SpamFrequencyPercent { get; init; }

    /// <summary>
    /// Frequency of this word in legitimate messages (percentage, 0-100)
    /// </summary>
    public required decimal LegitFrequencyPercent { get; init; }

    /// <summary>
    /// Spam-to-legit ratio (higher = better candidate)
    /// Calculated as: spamFreq / (legitFreq + 1)
    /// </summary>
    public required decimal SpamToLegitRatio { get; init; }

    /// <summary>
    /// Number of spam training samples containing this word
    /// </summary>
    public required int SpamSampleCount { get; init; }

    /// <summary>
    /// Number of legitimate messages containing this word
    /// </summary>
    public required int LegitSampleCount { get; init; }

    /// <summary>
    /// Total number of spam training samples analyzed
    /// </summary>
    public required int TotalSpamSamples { get; init; }

    /// <summary>
    /// Total number of legitimate messages analyzed
    /// </summary>
    public required int TotalLegitMessages { get; init; }
}
