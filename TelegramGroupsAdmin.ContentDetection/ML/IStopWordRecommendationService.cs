using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Service for generating ML-powered stop word recommendations
/// Analyzes spam/ham corpus to suggest additions, removals, and performance cleanup
/// </summary>
public interface IStopWordRecommendationService
{
    /// <summary>
    /// Generate comprehensive stop word recommendations based on analysis period
    /// </summary>
    /// <param name="since">Start of analysis period (default: last 30 days)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch of recommendations for additions, removals, and performance cleanup</returns>
    Task<StopWordRecommendationBatch> GenerateRecommendationsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
