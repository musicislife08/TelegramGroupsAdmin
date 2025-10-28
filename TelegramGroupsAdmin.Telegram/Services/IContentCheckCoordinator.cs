using SpamLibRequest = TelegramGroupsAdmin.ContentDetection.Models.ContentCheckRequest;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Coordinates content checking workflow by filtering trusted/admin users before content detection
/// Supports "always-run" critical checks that bypass trust/admin status (Phase 4.14)
/// Centralizes checking logic so it's consistent between bot and UI
/// </summary>
public interface IContentCheckCoordinator
{
    /// <summary>
    /// Run complete content check workflow: trust check → admin check → critical checks → regular checks
    /// Trusted users and admins skip regular checks but still run critical checks (URL filtering, file scanning)
    /// </summary>
    /// <param name="request">Content check request with message, user info, optional image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Coordinated result with trust/admin status, critical check violations, and spam detection results</returns>
    Task<ContentCheckCoordinatorResult> CheckAsync(SpamLibRequest request, CancellationToken cancellationToken = default);
}
