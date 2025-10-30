namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Service for generating ML-powered threshold optimization recommendations.
/// Analyzes veto patterns to identify algorithms that need threshold adjustments.
/// Returns DTOs that can be persisted by the caller.
/// </summary>
public interface IThresholdRecommendationService
{
    /// <summary>
    /// Generate threshold recommendations by analyzing detection results since the specified date.
    /// Returns list of recommendation DTOs (not persisted - caller handles saving).
    /// </summary>
    /// <param name="since">Start date for analysis period (typically 30 days ago)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of generated recommendation DTOs</returns>
    Task<List<ThresholdRecommendationDto>> GenerateRecommendationsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
