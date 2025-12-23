using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Repositories;

/// <summary>
/// Repository for managing browser push notification subscriptions
/// </summary>
public interface IPushSubscriptionsRepository
{
    /// <summary>
    /// Get all push subscriptions for a user (can have multiple browsers/devices)
    /// </summary>
    Task<IReadOnlyList<PushSubscription>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a subscription by its endpoint URL
    /// </summary>
    Task<PushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create or update a push subscription (upsert by user_id + endpoint)
    /// </summary>
    Task<PushSubscription> UpsertAsync(PushSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a subscription by endpoint (when user unsubscribes)
    /// </summary>
    Task<bool> DeleteByEndpointAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all subscriptions for a user
    /// </summary>
    Task<int> DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default);
}
