using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing review callback button contexts.
/// Contexts store review/chat/user data for DM buttons, enabling short callback IDs.
/// </summary>
public interface IReviewCallbackContextRepository
{
    /// <summary>
    /// Create a new callback context and return its ID.
    /// </summary>
    Task<long> CreateAsync(
        ReviewCallbackContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a callback context by ID.
    /// </summary>
    Task<ReviewCallbackContext?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a callback context by ID.
    /// </summary>
    Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all callback contexts for a review (cleanup when review is handled via web UI).
    /// </summary>
    Task DeleteByReviewIdAsync(
        long reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all expired callback contexts (cleanup job).
    /// </summary>
    Task<int> DeleteExpiredAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default);
}
