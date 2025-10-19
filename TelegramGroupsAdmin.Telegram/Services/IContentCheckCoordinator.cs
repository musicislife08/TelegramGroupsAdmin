using SpamLibRequest = TelegramGroupsAdmin.ContentDetection.Models.ContentCheckRequest;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

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

/// <summary>
/// Complete content check result including user trust/admin status, critical check violations, and spam detection
/// </summary>
public record ContentCheckCoordinatorResult
{
    /// <summary>
    /// Whether user is explicitly trusted (bypasses regular spam checks but NOT critical checks)
    /// </summary>
    public bool IsUserTrusted { get; init; }

    /// <summary>
    /// Whether user is a chat admin (bypasses regular spam checks but NOT critical checks)
    /// </summary>
    public bool IsUserAdmin { get; init; }

    /// <summary>
    /// Whether spam detection was skipped due to trust/admin status
    /// </summary>
    public bool SpamCheckSkipped { get; init; }

    /// <summary>
    /// Reason spam check was skipped (if applicable)
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Critical check violations (URL filtering, file scanning, etc.) that always run
    /// These violations should delete message + DM user, but NOT ban/warn for trusted/admin users
    /// </summary>
    public List<string> CriticalCheckViolations { get; init; } = new();

    /// <summary>
    /// Spam detection result (null if skipped)
    /// </summary>
    public ContentDetectionResult? SpamResult { get; init; }

    /// <summary>
    /// Overall determination: should this message be allowed?
    /// Message is blocked if either critical violations OR spam detection flags it
    /// </summary>
    public bool ShouldAllow => CriticalCheckViolations.Count == 0 && (SpamCheckSkipped || (SpamResult?.IsSpam == false));

    /// <summary>
    /// Overall determination: should this message be flagged as spam?
    /// </summary>
    public bool IsSpam => !SpamCheckSkipped && (SpamResult?.IsSpam ?? false);

    /// <summary>
    /// Whether message violates critical checks
    /// </summary>
    public bool HasCriticalViolations => CriticalCheckViolations.Count > 0;
}
