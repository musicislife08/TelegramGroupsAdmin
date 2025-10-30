using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing ML-generated threshold optimization recommendations
/// </summary>
public interface IThresholdRecommendationsRepository
{
    /// <summary>
    /// Insert a new threshold recommendation
    /// </summary>
    Task<long> InsertAsync(ThresholdRecommendation recommendation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recommendation by ID
    /// </summary>
    Task<ThresholdRecommendation?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all pending recommendations (status = 'pending')
    /// </summary>
    Task<List<ThresholdRecommendation>> GetPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recommendations by status
    /// </summary>
    Task<List<ThresholdRecommendation>> GetByStatusAsync(string status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recommendations for a specific algorithm
    /// </summary>
    Task<List<ThresholdRecommendation>> GetByAlgorithmAsync(string algorithmName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get most recent recommendation for each algorithm (status = 'pending')
    /// </summary>
    Task<List<ThresholdRecommendation>> GetLatestPendingByAlgorithmAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update recommendation status (approve, reject, apply)
    /// </summary>
    Task UpdateStatusAsync(
        long id,
        string status,
        string reviewedByUserId,
        string? reviewNotes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old recommendations (cleanup)
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of pending recommendations
    /// </summary>
    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all recommendations (for management UI)
    /// </summary>
    Task<List<ThresholdRecommendation>> GetAllRecommendationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new recommendation (convenience method for InsertAsync)
    /// </summary>
    Task AddRecommendationAsync(ThresholdRecommendation recommendation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply a recommendation (mark as applied)
    /// </summary>
    Task ApplyRecommendationAsync(long id, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reject a recommendation (mark as rejected)
    /// </summary>
    Task RejectRecommendationAsync(long id, string userId, string reason, CancellationToken cancellationToken = default);
}
